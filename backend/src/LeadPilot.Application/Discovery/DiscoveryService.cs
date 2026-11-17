using System.Text.Json;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeadPilot.Application.Discovery;

public sealed record DiscoveryLocation(string? State, string? City, string? District, string? Locality, string? PinCode)
{
    public string Describe() => string.Join(", ", new[] { Locality, City, District, State, PinCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public sealed record DiscoveryQuery
{
    public List<Guid> CategoryIds { get; init; } = [];
    /// <summary>Free-text keywords or business name, e.g. "CBSE coaching" or "ABC Clinic".</summary>
    public string? Keyword { get; init; }
    public List<DiscoveryLocation> Locations { get; init; } = [];
    /// <summary>any | with | without</summary>
    public string WebsiteFilter { get; init; } = "any";
    public bool RequirePhone { get; init; }
    public double? MinRating { get; init; }
    public bool SkipPermanentlyClosed { get; init; } = true;
}

public sealed record CreateDiscoveryJobRequest
{
    public string? Name { get; init; }
    public string Provider { get; init; } = "google_places";
    public required DiscoveryQuery Query { get; init; }
    public int MaxResults { get; init; } = 50;
    public bool AutoAnalyse { get; init; }
    /// <summary>Must be true when the estimate exceeds the configured confirmation threshold.</summary>
    public bool ConfirmCost { get; init; }
}

public sealed record DiscoveryEstimate(int Segments, int EstimatedRequests, decimal EstimatedCostUsd, bool RequiresConfirmation, decimal MonthSpendUsd, decimal MonthlyBudgetUsd, string Provider, bool ProviderConfigured, bool ProviderIsMock);

public sealed record DiscoveryJobDto(
    Guid Id, string Name, string Provider, DiscoveryJobStatus Status, int MaxResults, int TotalSegments, int CompletedSegments,
    int FoundCount, int CreatedCount, int DuplicateCount, int SkippedCount, int ErrorCount, decimal EstimatedCostUsd, decimal ActualCostUsd,
    string? Error, DateTime CreatedAt, DateTime? StartedAt, DateTime? CompletedAt, DiscoveryQuery Query, double Progress);

public sealed record DiscoveryResultDto(Guid Id, Guid? LeadId, string BusinessName, DiscoveryResultOutcome Outcome, string? Message, DateTime CreatedAt);

public sealed record ProviderStatusDto(string Name, string DisplayName, bool Configured, bool IsMock, decimal CostPerRequestUsd, string Attribution);

internal sealed record DiscoverySegment(string Keyword, DiscoveryLocation Location, Guid? CategoryId);

internal sealed record DiscoveryCursor(int SegmentIndex, string? PageToken, int SegmentFound);

public sealed class DiscoveryService(
    IAppDbContext db,
    IEnumerable<ILeadDiscoveryProvider> providers,
    ISettingsService settings,
    IUsageTracker usage,
    IJobScheduler jobs,
    ICurrentUser currentUser,
    IAuditService audit,
    IClock clock)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<ProviderStatusDto> ProviderStatus() =>
        providers.Select(p => new ProviderStatusDto(p.Name, p.DisplayName, p.IsConfigured, p.IsMock, p.EstimatedCostPerRequestUsd, p.Attribution)).ToList();

    public ILeadDiscoveryProvider ResolveProvider(string name) =>
        providers.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ValidationFailedException($"Unknown discovery provider '{name}'.");

    internal async Task<List<DiscoverySegment>> BuildSegmentsAsync(DiscoveryQuery q, CancellationToken ct)
    {
        var categories = q.CategoryIds.Count == 0
            ? []
            : await db.LeadCategories.AsNoTracking().Where(c => q.CategoryIds.Contains(c.Id)).ToListAsync(ct);
        var keywords = new List<(string Keyword, Guid? CategoryId)>();
        foreach (var c in categories)
        {
            var kw = string.IsNullOrWhiteSpace(c.SearchKeywords) ? c.Name : c.SearchKeywords.Split(',')[0].Trim();
            keywords.Add((string.IsNullOrWhiteSpace(q.Keyword) ? kw : $"{q.Keyword} {kw}", c.Id));
        }
        if (keywords.Count == 0 && !string.IsNullOrWhiteSpace(q.Keyword)) keywords.Add((q.Keyword.Trim(), null));
        var locations = q.Locations.Count == 0 ? [new DiscoveryLocation(null, null, null, null, null)] : q.Locations;
        return keywords.SelectMany(k => locations.Select(l => new DiscoverySegment(k.Keyword, l, k.CategoryId))).ToList();
    }

    public async Task<DiscoveryEstimate> EstimateAsync(CreateDiscoveryJobRequest r, CancellationToken ct)
    {
        Validate(r);
        var provider = ResolveProvider(r.Provider);
        var segments = await BuildSegmentsAsync(r.Query, ct);
        var perSegment = Math.Min(provider.MaxResultsPerQuery, (int)Math.Ceiling(r.MaxResults / (double)Math.Max(1, segments.Count)));
        var pageSize = 20;
        var requests = segments.Count * Math.Max(1, (int)Math.Ceiling(perSegment / (double)pageSize));
        var cost = requests * provider.EstimatedCostPerRequestUsd;
        var budget = await settings.GetAsync<BudgetSettings>(BudgetSettings.Key, ct);
        var monthStart = new DateTime(clock.UtcNow.Year, clock.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var spend = await usage.GetSpendAsync(provider.Name, monthStart, ct);
        return new DiscoveryEstimate(segments.Count, requests, Math.Round(cost, 4), cost > budget.ConfirmDiscoveryAboveUsd, spend,
            budget.MonthlyDiscoveryBudgetUsd, provider.Name, provider.IsConfigured, provider.IsMock);
    }

    private static void Validate(CreateDiscoveryJobRequest r)
    {
        if (r.MaxResults is < 1 or > 1000) throw new ValidationFailedException("MaxResults must be between 1 and 1000.");
        if (r.Query.CategoryIds.Count == 0 && string.IsNullOrWhiteSpace(r.Query.Keyword))
            throw new ValidationFailedException("Select at least one category or enter a keyword.");
        if (r.Query.Locations.Count > 50) throw new ValidationFailedException("At most 50 locations per job.");
        if (r.Query.WebsiteFilter is not ("any" or "with" or "without")) throw new ValidationFailedException("WebsiteFilter must be any, with or without.");
        foreach (var l in r.Query.Locations)
            if (l.PinCode is { Length: > 0 } pin && !System.Text.RegularExpressions.Regex.IsMatch(pin, "^[1-9][0-9]{5}$"))
                throw new ValidationFailedException($"Invalid PIN code '{pin}'.");
    }

    public async Task<DiscoveryJobDto> CreateJobAsync(CreateDiscoveryJobRequest r, CancellationToken ct)
    {
        var estimate = await EstimateAsync(r, ct);
        var provider = ResolveProvider(r.Provider);
        if (!provider.IsConfigured)
            throw new IntegrationNotConfiguredException(provider.DisplayName, "Add the API key in the server environment (see docs/SETUP-GOOGLE-PLACES.md) or import leads via CSV.");
        if (estimate.RequiresConfirmation && !r.ConfirmCost)
            throw new ValidationFailedException($"Estimated cost ${estimate.EstimatedCostUsd:0.00} exceeds your confirmation threshold. Confirm to continue.",
                new Dictionary<string, string[]> { ["confirmCost"] = ["required"] });
        var budget = await settings.GetAsync<BudgetSettings>(BudgetSettings.Key, ct);
        if (budget.BlockDiscoveryWhenBudgetReached && !provider.IsMock && estimate.MonthSpendUsd + estimate.EstimatedCostUsd > budget.MonthlyDiscoveryBudgetUsd)
            throw new BudgetExceededException($"This job would exceed the monthly discovery budget (${budget.MonthlyDiscoveryBudgetUsd:0.00}). Increase the budget in Settings to continue.");

        var name = string.IsNullOrWhiteSpace(r.Name)
            ? $"{r.Query.Keyword ?? "Discovery"} — {string.Join(", ", r.Query.Locations.Take(3).Select(l => l.Describe()))}".Trim(' ', '—')
            : r.Name.Trim();
        var job = new DiscoveryJob
        {
            OwnerUserId = currentUser.RequireUserId(),
            Name = name.Length > 200 ? name[..200] : name,
            Provider = provider.Name,
            QueryJson = JsonSerializer.Serialize(r.Query, Json),
            MaxResults = r.MaxResults,
            TotalSegments = estimate.Segments,
            EstimatedCostUsd = estimate.EstimatedCostUsd,
            AutoAnalyse = r.AutoAnalyse
        };
        db.DiscoveryJobs.Add(job);
        await db.SaveChangesAsync(ct);
        job.HangfireJobId = jobs.Enqueue<DiscoveryJobRunner>(x => x.RunAsync(job.Id, CancellationToken.None));
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("discovery.create", nameof(DiscoveryJob), job.Id.ToString(), $"provider={provider.Name} max={r.MaxResults}", ct);
        return ToDto(job);
    }

    public async Task<PagedResult<DiscoveryJobDto>> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        var total = await db.DiscoveryJobs.CountAsync(ct);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var jobsList = await db.DiscoveryJobs.AsNoTracking().OrderByDescending(j => j.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<DiscoveryJobDto>(jobsList.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<DiscoveryJobDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.DiscoveryJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct) ?? throw new NotFoundException("Discovery job", id));

    public async Task<PagedResult<DiscoveryResultDto>> ResultsAsync(Guid id, int page, int pageSize, CancellationToken ct)
    {
        var q = db.DiscoveryJobResults.AsNoTracking().Where(r => r.DiscoveryJobId == id);
        var total = await q.CountAsync(ct);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var items = await q.OrderByDescending(r => r.CreatedAt).Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
            .Select(r => new DiscoveryResultDto(r.Id, r.LeadId, r.BusinessName, r.Outcome, r.Message, r.CreatedAt)).ToListAsync(ct);
        return new PagedResult<DiscoveryResultDto>(items, page, pageSize, total);
    }

    public async Task<DiscoveryJobDto> PauseAsync(Guid id, CancellationToken ct)
    {
        var job = await db.DiscoveryJobs.FirstOrDefaultAsync(j => j.Id == id, ct) ?? throw new NotFoundException("Discovery job", id);
        if (job.Status is not (DiscoveryJobStatus.Running or DiscoveryJobStatus.Pending)) throw new ConflictException($"Job is {job.Status}.");
        job.Status = DiscoveryJobStatus.Paused;
        await db.SaveChangesAsync(ct);
        return ToDto(job);
    }

    public async Task<DiscoveryJobDto> ResumeAsync(Guid id, CancellationToken ct)
    {
        var job = await db.DiscoveryJobs.FirstOrDefaultAsync(j => j.Id == id, ct) ?? throw new NotFoundException("Discovery job", id);
        if (job.Status is not (DiscoveryJobStatus.Paused or DiscoveryJobStatus.Failed)) throw new ConflictException($"Job is {job.Status}.");
        job.Status = DiscoveryJobStatus.Pending;
        job.Error = null;
        await db.SaveChangesAsync(ct);
        job.HangfireJobId = jobs.Enqueue<DiscoveryJobRunner>(x => x.RunAsync(job.Id, CancellationToken.None));
        await db.SaveChangesAsync(ct);
        return ToDto(job);
    }

    public async Task<DiscoveryJobDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var job = await db.DiscoveryJobs.FirstOrDefaultAsync(j => j.Id == id, ct) ?? throw new NotFoundException("Discovery job", id);
        if (job.Status is DiscoveryJobStatus.Completed or DiscoveryJobStatus.Cancelled) return ToDto(job);
        job.Status = DiscoveryJobStatus.Cancelled;
        job.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(job);
    }

    public static DiscoveryJobDto ToDto(DiscoveryJob j)
    {
        var query = JsonSerializer.Deserialize<DiscoveryQuery>(j.QueryJson, Json) ?? new DiscoveryQuery();
        var progress = j.Status == DiscoveryJobStatus.Completed ? 1.0 :
            j.MaxResults == 0 ? 0 : Math.Min(1.0, Math.Max(j.CompletedSegments / (double)Math.Max(1, j.TotalSegments), j.FoundCount / (double)j.MaxResults));
        return new DiscoveryJobDto(j.Id, j.Name, j.Provider, j.Status, j.MaxResults, j.TotalSegments, j.CompletedSegments, j.FoundCount,
            j.CreatedCount, j.DuplicateCount, j.SkippedCount, j.ErrorCount, j.EstimatedCostUsd, j.ActualCostUsd, j.Error, j.CreatedAt,
            j.StartedAt, j.CompletedAt, query, Math.Round(progress, 3));
    }
}
