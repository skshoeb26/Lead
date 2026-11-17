using System.Text;
using System.Text.Json;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Analysis;
using LeadPilot.Application.Common;
using LeadPilot.Application.Crm;
using LeadPilot.Application.Intelligence;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeadPilot.Application.Pitches;

public sealed record GeneratePitchRequest(Guid LeadId, PitchType Type, PitchLanguage Language = PitchLanguage.English, List<Guid>? OpportunityIds = null, string? ExtraInstructions = null);
public sealed record UpdatePitchRequest(string? Subject, string Body);
public sealed record PitchDto(Guid Id, Guid LeadId, string LeadName, PitchType Type, PitchLanguage Language, string? Subject, string Body,
    string? Provider, string? Model, string? PromptVersion, bool IsEdited, DateTime CreatedAt, DateTime UpdatedAt, IReadOnlyList<Guid> OpportunityIds, bool IsMock);

public sealed class PitchService(
    IAppDbContext db,
    IAIProvider ai,
    LeadAnalysisPipeline pipeline,
    ISettingsService settings,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<PitchDto> GenerateAsync(GeneratePitchRequest r, CancellationToken ct)
    {
        if (r.ExtraInstructions is { Length: > 500 }) throw new ValidationFailedException("Extra instructions are limited to 500 characters.");
        var lead = await db.Leads.Include(l => l.Category).FirstOrDefaultAsync(l => l.Id == r.LeadId, ct) ?? throw new NotFoundException("Lead", r.LeadId);
        var opps = await db.Opportunities.AsNoTracking()
            .Where(o => o.LeadId == r.LeadId && !o.IsDismissed && (r.OpportunityIds == null || r.OpportunityIds.Count == 0 || r.OpportunityIds.Contains(o.Id)))
            .OrderByDescending(o => o.Confidence).Take(4).ToListAsync(ct);
        if (opps.Count == 0)
            throw new ValidationFailedException("No evidence-based opportunities for this lead yet. Run website/AI analysis first so the pitch can reference a genuine observation.");

        await pipeline.EnsureAIAllowedAsync(ct);
        var owner = await settings.GetAsync<OwnerProfile>(OwnerProfile.Key, ct);
        var analysis = await db.AIAnalyses.AsNoTracking().Where(a => a.LeadId == r.LeadId && a.Status == ProcessStatus.Completed)
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        string? previousSubject = null;
        if (r.Type == PitchType.FollowUp)
            previousSubject = await db.EmailDrafts.AsNoTracking().Where(e => e.LeadId == r.LeadId && e.Status == EmailStatus.Sent)
                .OrderByDescending(e => e.SentAt).Select(e => e.Subject).FirstOrDefaultAsync(ct);

        var prompt = BuildPrompt(lead, opps, analysis, owner, r, previousSubject);
        var response = await ai.CompleteJsonAsync(new AIJsonRequest
        {
            Purpose = $"pitch-{r.Type}", PromptVersion = PromptLibrary.PitchVersion, SystemPrompt = PromptLibrary.PitchSystem(r.Language),
            UserPrompt = prompt, JsonSchema = AIJson.PitchSchema, LeadId = r.LeadId, MaxTokens = r.Type is PitchType.ProposalOutline or PitchType.Quotation or PitchType.WebsiteProposal ? 6000 : 3000
        }, ct);

        var (result, errors) = AIResultValidator.ParsePitch(response.Json, lead.BusinessName, PromptLibrary.RequiresSubject(r.Type));
        if (result is null) throw new ValidationFailedException("The generated draft did not meet quality rules: " + string.Join(" ", errors) + " Please regenerate.");

        var pitch = new PitchDraft
        {
            OwnerUserId = currentUser.UserId ?? lead.OwnerUserId, LeadId = lead.Id, Type = r.Type, Language = r.Language,
            Subject = PromptLibrary.RequiresSubject(r.Type) ? result.Subject?.Trim() : null, Body = result.Body.Trim(),
            AIAnalysisId = analysis?.Id, OpportunityIdsJson = JsonSerializer.Serialize(opps.Select(o => o.Id)),
            Provider = response.Provider, Model = response.Model, PromptVersion = PromptLibrary.PitchVersion
        };
        db.PitchDrafts.Add(pitch);
        if (lead.Stage is PipelineStage.New or PipelineStage.Analysed or PipelineStage.Qualified) lead.Stage = PipelineStage.PitchReady;
        db.CrmActivities.Add(Activity.Create(lead.Id, ActivityType.System, $"{r.Type} draft generated", $"Language: {r.Language}", currentUser.UserId));
        await db.SaveChangesAsync(ct);
        return ToDto(pitch, lead.BusinessName);
    }

    private static string BuildPrompt(Lead lead, List<Opportunity> opps, AIAnalysis? analysis, OwnerProfile owner, GeneratePitchRequest r, string? previousSubject)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Draft type: {PromptLibrary.PitchInstruction(r.Type)}");
        sb.AppendLine().AppendLine("## Prospect");
        sb.AppendLine($"Business name: {lead.BusinessName}");
        sb.AppendLine($"Category: {lead.Category?.Name ?? "unknown"}");
        sb.AppendLine($"City: {lead.City ?? "unknown"}");
        sb.AppendLine($"Website: {lead.WebsiteUrl ?? "none listed"}");
        if (analysis?.BusinessSummary is not null) sb.AppendLine($"Summary: {analysis.BusinessSummary}");
        sb.AppendLine().AppendLine("## Observed opportunities (reference at least one, using its evidence honestly)");
        foreach (var o in opps)
            sb.AppendLine($"- {o.Title}: {o.Problem} Evidence: {o.Evidence}{(o.Assumptions is null ? "" : $" (Caveat: {o.Assumptions})")} Suggested solution: {o.RecommendedService}");
        if (analysis?.SuggestedApproach is not null) sb.AppendLine($"Suggested approach: {analysis.SuggestedApproach}");
        if (previousSubject is not null) sb.AppendLine($"Previous email subject: {previousSubject}");
        sb.AppendLine().AppendLine("## Sender (use exactly; omit anything blank)");
        sb.AppendLine($"Name: {owner.Name}");
        sb.AppendLine($"Business: {owner.BusinessName}");
        sb.AppendLine($"Portfolio: {owner.PortfolioUrl}");
        sb.AppendLine($"Phone: {owner.Phone}");
        if (!string.IsNullOrWhiteSpace(owner.Signature)) sb.AppendLine($"Signature block:\n{owner.Signature}");
        if (r.Type == PitchType.Quotation) sb.AppendLine($"Target project range: INR {owner.TargetProjectMin:N0}–{owner.TargetProjectMax:N0} (indicative).");
        if (!string.IsNullOrWhiteSpace(r.ExtraInstructions)) sb.AppendLine().AppendLine($"Additional guidance from the sender: {r.ExtraInstructions}");
        sb.AppendLine().AppendLine("Respond with the JSON object only.");
        return sb.ToString();
    }

    public async Task<PagedResult<PitchDto>> ListAsync(Guid? leadId, PitchType? type, int page, int pageSize, CancellationToken ct)
    {
        var q = db.PitchDrafts.AsNoTracking().Where(p => !p.IsArchived && (leadId == null || p.LeadId == leadId) && (type == null || p.Type == type));
        var total = await q.CountAsync(ct);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var items = await q.OrderByDescending(p => p.UpdatedAt).Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
            .Select(p => new { p, Name = p.Lead != null ? p.Lead.BusinessName : "" }).ToListAsync(ct);
        return new PagedResult<PitchDto>(items.Select(x => ToDto(x.p, x.Name)).ToList(), page, pageSize, total);
    }

    public async Task<PitchDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.PitchDrafts.AsNoTracking().Include(x => x.Lead).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Pitch", id);
        return ToDto(p, p.Lead?.BusinessName ?? "");
    }

    public async Task<PitchDto> UpdateAsync(Guid id, UpdatePitchRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Body) || r.Body.Length > 20000) throw new ValidationFailedException("Body is required (max 20,000 characters).");
        if (r.Subject is { Length: > 200 }) throw new ValidationFailedException("Subject max 200 characters.");
        var p = await db.PitchDrafts.Include(x => x.Lead).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Pitch", id);
        p.Subject = r.Subject?.Trim();
        p.Body = r.Body.Trim();
        p.IsEdited = true;
        p.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(p, p.Lead?.BusinessName ?? "");
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var p = await db.PitchDrafts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Pitch", id);
        p.IsArchived = true;
        await db.SaveChangesAsync(ct);
    }

    private static PitchDto ToDto(PitchDraft p, string leadName) => new(p.Id, p.LeadId, leadName, p.Type, p.Language, p.Subject, p.Body, p.Provider, p.Model,
        p.PromptVersion, p.IsEdited, p.CreatedAt, p.UpdatedAt,
        string.IsNullOrEmpty(p.OpportunityIdsJson) ? [] : JsonSerializer.Deserialize<List<Guid>>(p.OpportunityIdsJson) ?? [], p.Provider == "mock");
}
