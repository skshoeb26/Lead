using System.Diagnostics;
using System.Text.Json;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Analysis;
using LeadPilot.Application.Common;
using LeadPilot.Application.Intelligence;
using LeadPilot.Application.Leads;
using LeadPilot.Application.Notifications;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeadPilot.Application.Discovery;

/// <summary>
/// Executes a discovery job segment by segment, page by page. Progress and a resume cursor are persisted after every
/// page, so the job survives restarts, can be paused/cancelled, and a provider failure on one segment never stops the rest.
/// </summary>
public sealed class DiscoveryJobRunner(
    IAppDbContext db,
    DiscoveryService discovery,
    LeadService leads,
    IUsageTracker usage,
    ISettingsService settings,
    NotificationService notifications,
    IJobScheduler jobs,
    IClock clock,
    ILogger<DiscoveryJobRunner> logger)
{
    private const int MaxConsecutiveSegmentFailures = 3;

    public async Task RunAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.DiscoveryJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.Status is DiscoveryJobStatus.Completed or DiscoveryJobStatus.Cancelled or DiscoveryJobStatus.Paused) return;

        var provider = discovery.ResolveProvider(job.Provider);
        var query = JsonSerializer.Deserialize<DiscoveryQuery>(job.QueryJson, DiscoveryService.Json) ?? new DiscoveryQuery();
        var segments = await discovery.BuildSegmentsAsync(query, ct);
        var cursor = job.CursorJson is null ? new DiscoveryCursor(0, null, 0) : JsonSerializer.Deserialize<DiscoveryCursor>(job.CursorJson, DiscoveryService.Json)!;
        var categories = await db.LeadCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);

        job.Status = DiscoveryJobStatus.Running;
        job.StartedAt ??= clock.UtcNow;
        job.TotalSegments = segments.Count;
        await db.SaveChangesAsync(ct);

        var perSegmentTarget = Math.Max(1, (int)Math.Ceiling(job.MaxResults / (double)Math.Max(1, segments.Count)));
        var consecutiveFailures = 0;
        var newLeadIds = new List<Guid>();

        try
        {
            while (cursor.SegmentIndex < segments.Count && job.FoundCount < job.MaxResults)
            {
                if (await IsStoppedAsync(job, ct)) return;
                var segment = segments[cursor.SegmentIndex];
                DiscoverySearchPage page;
                var sw = Stopwatch.StartNew();
                try
                {
                    page = await provider.SearchAsync(new DiscoverySearchRequest(segment.Keyword, segment.Location.Locality, segment.Location.City,
                        segment.Location.District, segment.Location.State, segment.Location.PinCode,
                        Math.Min(20, perSegmentTarget - cursor.SegmentFound), cursor.PageToken), ct);
                    consecutiveFailures = 0;
                    await usage.RecordAsync(new UsageLog { Provider = provider.Name, Operation = "search", JobId = job.Id, CostUsd = page.CostUsd, DurationMs = (int)sw.ElapsedMilliseconds }, ct);
                    job.ActualCostUsd += page.CostUsd;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    consecutiveFailures++;
                    job.ErrorCount++;
                    logger.LogWarning(ex, "Discovery segment {Segment} failed for job {JobId}", cursor.SegmentIndex, job.Id);
                    await usage.RecordAsync(new UsageLog { Provider = provider.Name, Operation = "search", JobId = job.Id, Success = false, Error = Truncate(ex.Message), DurationMs = (int)sw.ElapsedMilliseconds }, ct);
                    db.DiscoveryJobResults.Add(new DiscoveryJobResult { DiscoveryJobId = job.Id, BusinessName = $"[{segment.Keyword} / {segment.Location.Describe()}]", Outcome = DiscoveryResultOutcome.Error, Message = Truncate(ex.Message) });
                    if (consecutiveFailures >= MaxConsecutiveSegmentFailures) throw new InvalidOperationException($"Provider failed {consecutiveFailures} times in a row: {ex.Message}", ex);
                    cursor = new DiscoveryCursor(cursor.SegmentIndex + 1, null, 0);
                    job.CompletedSegments = cursor.SegmentIndex;
                    job.CursorJson = JsonSerializer.Serialize(cursor, DiscoveryService.Json);
                    await db.SaveChangesAsync(ct);
                    continue;
                }

                foreach (var business in page.Results)
                {
                    if (job.FoundCount >= job.MaxResults) break;
                    job.FoundCount++;
                    cursor = cursor with { SegmentFound = cursor.SegmentFound + 1 };
                    var outcome = await ProcessBusinessAsync(job, provider, business, segment, query, categories, ct);
                    if (outcome.LeadId is not null && outcome.Outcome == DiscoveryResultOutcome.Created) newLeadIds.Add(outcome.LeadId.Value);
                }

                var segmentDone = page.NextPageToken is null || cursor.SegmentFound >= perSegmentTarget;
                cursor = segmentDone ? new DiscoveryCursor(cursor.SegmentIndex + 1, null, 0) : cursor with { PageToken = page.NextPageToken };
                job.CompletedSegments = cursor.SegmentIndex;
                job.CursorJson = JsonSerializer.Serialize(cursor, DiscoveryService.Json);
                await db.SaveChangesAsync(ct);
            }

            job.Status = DiscoveryJobStatus.Completed;
            job.CompletedAt = clock.UtcNow;
            job.CompletedSegments = segments.Count;
            await db.SaveChangesAsync(ct);

            await notifications.NotifyAsync(job.OwnerUserId, NotificationType.DiscoveryCompleted, "Discovery complete",
                $"{job.CreatedCount} new leads found.", $"{job.Name}: {job.CreatedCount} new, {job.DuplicateCount} duplicates.", $"/discover/jobs/{job.Id}", ct);

            var automation = await settings.GetAsync<AutomationSettings>(AutomationSettings.Key, ct);
            if (job.AutoAnalyse || automation.AutoWebsiteAnalysisAfterDiscovery)
                foreach (var id in newLeadIds)
                    jobs.Enqueue<LeadAnalysisPipeline>(p => p.AnalyseWebsiteAsync(id, true, CancellationToken.None));
        }
        catch (OperationCanceledException)
        {
            throw; // Hangfire will retry / resume from cursor
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discovery job {JobId} failed", job.Id);
            job.Status = DiscoveryJobStatus.Failed;
            job.Error = Truncate(ex.Message);
            await db.SaveChangesAsync(CancellationToken.None);
            await notifications.NotifyAsync(job.OwnerUserId, NotificationType.JobError, "Discovery job failed", "A discovery job failed. Tap to view.",
                $"{job.Name}: {job.Error}", $"/discover/jobs/{job.Id}", CancellationToken.None);
        }
    }

    private async Task<bool> IsStoppedAsync(DiscoveryJob job, CancellationToken ct)
    {
        var status = await db.DiscoveryJobs.AsNoTracking().Where(j => j.Id == job.Id).Select(j => j.Status).FirstAsync(ct);
        if (status is DiscoveryJobStatus.Paused or DiscoveryJobStatus.Cancelled)
        {
            job.Status = status;
            await db.SaveChangesAsync(ct);
            return true;
        }
        return false;
    }

    private async Task<(DiscoveryResultOutcome Outcome, Guid? LeadId)> ProcessBusinessAsync(
        DiscoveryJob job, ILeadDiscoveryProvider provider, DiscoveredBusiness b, DiscoverySegment segment, DiscoveryQuery query,
        IReadOnlyDictionary<Guid, LeadCategory> categories, CancellationToken ct)
    {
        string? skip = null;
        if (query.SkipPermanentlyClosed && string.Equals(b.BusinessStatus, "CLOSED_PERMANENTLY", StringComparison.OrdinalIgnoreCase)) skip = "Permanently closed";
        else if (query.WebsiteFilter == "with" && string.IsNullOrWhiteSpace(b.WebsiteUrl)) skip = "No website (filter: with website)";
        else if (query.WebsiteFilter == "without" && !string.IsNullOrWhiteSpace(b.WebsiteUrl)) skip = "Has website (filter: without website)";
        else if (query.RequirePhone && string.IsNullOrWhiteSpace(b.Phone)) skip = "No phone (filter)";
        else if (query.MinRating is not null && (b.Rating ?? 0) < query.MinRating) skip = $"Rating below {query.MinRating}";

        if (skip is not null)
        {
            job.SkippedCount++;
            db.DiscoveryJobResults.Add(new DiscoveryJobResult { DiscoveryJobId = job.Id, ExternalId = b.ExternalId, BusinessName = b.Name, Outcome = DiscoveryResultOutcome.Skipped, Message = skip });
            return (DiscoveryResultOutcome.Skipped, null);
        }

        var probe = new DuplicateProbe(provider.Name, b.ExternalId, b.Name, b.WebsiteUrl, b.Phone, b.City ?? segment.Location.City, b.PinCode);
        var duplicates = await leads.FindDuplicatesAsync(probe, null, ct);
        var exact = duplicates.FirstOrDefault(d => d.IsExact);
        if (exact is not null)
        {
            job.DuplicateCount++;
            if (!await db.LeadSources.AnyAsync(s => s.LeadId == exact.LeadId && s.Provider == provider.Name && s.ExternalId == b.ExternalId, ct))
                db.LeadSources.Add(new LeadSource { LeadId = exact.LeadId, Provider = provider.Name, ExternalId = b.ExternalId, SourceUrl = b.SourceUrl, Attribution = provider.Attribution, DiscoveryJobId = job.Id });
            db.DiscoveryJobResults.Add(new DiscoveryJobResult { DiscoveryJobId = job.Id, LeadId = exact.LeadId, ExternalId = b.ExternalId, BusinessName = b.Name, Outcome = DiscoveryResultOutcome.Duplicate, Message = string.Join("; ", exact.Reasons) });
            return (DiscoveryResultOutcome.Duplicate, exact.LeadId);
        }

        var lead = leads.BuildLead(new CreateLeadRequest
        {
            BusinessName = b.Name,
            CategoryId = segment.CategoryId,
            WebsiteUrl = b.WebsiteUrl,
            AddressLine = b.FormattedAddress,
            Locality = b.Locality ?? segment.Location.Locality,
            City = b.City ?? segment.Location.City,
            District = b.District ?? segment.Location.District,
            State = b.State ?? segment.Location.State,
            PinCode = b.PinCode,
            ContactSourceUrl = b.SourceUrl
        }, provider.Name, job.OwnerUserId);
        lead.ProviderPlaceId = b.ExternalId;
        lead.Latitude = b.Latitude; lead.Longitude = b.Longitude;
        lead.Rating = b.Rating; lead.ReviewCount = b.ReviewCount; lead.BusinessStatus = b.BusinessStatus;
        lead.LastVerifiedAt = clock.UtcNow;
        lead.ContactDiscoveryStatus = ProcessStatus.Completed;
        lead.Sources[0].ExternalId = b.ExternalId;
        lead.Sources[0].Attribution = provider.Attribution;
        lead.Sources[0].DiscoveryJobId = job.Id;
        if (!string.IsNullOrWhiteSpace(b.Phone)) LeadService.AttachContact(lead, ContactType.Phone, b.Phone, "Business phone", provider.Name, b.SourceUrl, true);
        if (!string.IsNullOrWhiteSpace(b.Email) && Normalizer.IsValidEmailSyntax(b.Email)) LeadService.AttachContact(lead, ContactType.Email, b.Email, "Business email", provider.Name, b.SourceUrl, true);
        if (!string.IsNullOrWhiteSpace(lead.WebsiteUrl)) LeadService.AttachContact(lead, ContactType.Website, lead.WebsiteUrl, "Website", provider.Name, b.SourceUrl, true);

        db.Leads.Add(lead);
        db.CrmActivities.Add(Crm.Activity.Create(lead.Id, ActivityType.System, "Lead discovered", $"{provider.DisplayName}: {segment.Keyword} in {segment.Location.Describe()}"));
        var fuzzy = duplicates.FirstOrDefault();
        db.DiscoveryJobResults.Add(new DiscoveryJobResult
        {
            DiscoveryJobId = job.Id, LeadId = lead.Id, ExternalId = b.ExternalId, BusinessName = b.Name, Outcome = DiscoveryResultOutcome.Created,
            Message = fuzzy is null ? null : $"Possible duplicate of {fuzzy.BusinessName} ({string.Join("; ", fuzzy.Reasons)})"
        });
        job.CreatedCount++;
        _ = categories;
        return (DiscoveryResultOutcome.Created, lead.Id);
    }

    private static string Truncate(string s) => s.Length > 1000 ? s[..1000] : s;
}
