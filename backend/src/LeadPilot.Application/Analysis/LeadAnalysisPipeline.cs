using System.Text;
using System.Text.Json;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using LeadPilot.Application.Crm;
using LeadPilot.Application.Intelligence;
using LeadPilot.Application.Leads;
using LeadPilot.Application.Notifications;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeadPilot.Application.Analysis;

public sealed record AnalyseRequest(bool Website = true, bool AI = true, bool UsePageSpeed = true);
public sealed record AnalysisQueuedDto(Guid LeadId, bool WebsiteQueued, bool AIQueued, string? Message);

public sealed class LeadAnalysisPipeline(
    IAppDbContext db,
    IWebsiteAnalyzer analyzer,
    IAIProvider ai,
    ILeadScoringService scoring,
    ISettingsService settings,
    IUsageTracker usage,
    NotificationService notifications,
    IJobScheduler jobs,
    IClock clock,
    ILogger<LeadAnalysisPipeline> logger)
{
    // ------------------------------------------------------------------ queueing (API)

    public async Task<AnalysisQueuedDto> QueueAsync(Guid leadId, AnalyseRequest r, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct) ?? throw new NotFoundException("Lead", leadId);
        var websiteQueued = false;
        var aiQueued = false;
        if (r.Website && lead.WebsiteUrl is not null)
        {
            lead.WebsiteAnalysisStatus = ProcessStatus.Queued;
            websiteQueued = true;
        }
        if (r.AI)
        {
            await EnsureAIAllowedAsync(ct);
            lead.AIAnalysisStatus = ProcessStatus.Queued;
            aiQueued = true;
        }
        await db.SaveChangesAsync(ct);

        if (websiteQueued) jobs.Enqueue<LeadAnalysisPipeline>(p => p.AnalyseWebsiteAsync(leadId, aiQueued, CancellationToken.None));
        else if (aiQueued) jobs.Enqueue<LeadAnalysisPipeline>(p => p.RunAIAnalysisAsync(leadId, CancellationToken.None));
        else jobs.Enqueue<LeadAnalysisPipeline>(p => p.ScoreAsync(leadId, CancellationToken.None));

        return new AnalysisQueuedDto(leadId, websiteQueued, aiQueued,
            lead.WebsiteUrl is null && r.Website ? "Lead has no website; website analysis skipped." : null);
    }

    public async Task<int> QueueBulkAsync(IReadOnlyList<Guid> leadIds, AnalyseRequest r, CancellationToken ct)
    {
        if (leadIds.Count > 200) throw new ValidationFailedException("At most 200 leads per bulk analysis.");
        var n = 0;
        foreach (var id in leadIds) { await QueueAsync(id, r, ct); n++; }
        return n;
    }

    // ------------------------------------------------------------------ website analysis (job)

    public async Task AnalyseWebsiteAsync(Guid leadId, bool thenAI, CancellationToken ct)
    {
        var lead = await db.Leads.Include(l => l.Category).FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return;
        if (string.IsNullOrWhiteSpace(lead.WebsiteUrl))
        {
            lead.WebsiteAnalysisStatus = ProcessStatus.Skipped;
            await ReplaceRuleOpportunitiesAsync(lead, null, ct);
            await db.SaveChangesAsync(ct);
            await ContinueAsync(leadId, thenAI, ct);
            return;
        }

        lead.WebsiteAnalysisStatus = ProcessStatus.Running;
        await db.SaveChangesAsync(ct);
        var automation = await settings.GetAsync<AutomationSettings>(AutomationSettings.Key, ct);

        WebsiteAnalysis analysis;
        try
        {
            var result = await analyzer.AnalyseAsync(leadId, lead.WebsiteUrl, automation.UsePageSpeed, ct);
            analysis = result.Analysis;
            analysis.Evidence = result.Evidence.ToList();
            db.WebsiteAnalyses.Add(analysis);
            lead.WebsiteAnalysisStatus = analysis.Status;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Website analysis failed for lead {LeadId}", leadId);
            analysis = new WebsiteAnalysis
            {
                LeadId = leadId, RequestedUrl = lead.WebsiteUrl, Status = ProcessStatus.Failed,
                Error = ex is UnsafeUrlException ? $"Blocked: {ex.Message}" : ex.Message, AnalysedAt = clock.UtcNow
            };
            db.WebsiteAnalyses.Add(analysis);
            lead.WebsiteAnalysisStatus = ProcessStatus.Failed;
        }

        await ReplaceRuleOpportunitiesAsync(lead, analysis, ct);
        if (lead.Stage == PipelineStage.New) lead.Stage = PipelineStage.Analysed;
        db.CrmActivities.Add(Activity.Create(leadId, ActivityType.Analysis, "Website analysed",
            analysis.Status == ProcessStatus.Completed ? $"{analysis.Evidence.Count} checks recorded" : $"Failed: {analysis.Error}"));
        await db.SaveChangesAsync(ct);
        await ContinueAsync(leadId, thenAI, ct);
    }

    private async Task ContinueAsync(Guid leadId, bool thenAI, CancellationToken ct)
    {
        var automation = await settings.GetAsync<AutomationSettings>(AutomationSettings.Key, ct);
        if (thenAI || automation.AutoAIAnalysisAfterWebsiteAnalysis)
        {
            try
            {
                await EnsureAIAllowedAsync(ct);
                jobs.Enqueue<LeadAnalysisPipeline>(p => p.RunAIAnalysisAsync(leadId, CancellationToken.None));
                return;
            }
            catch (AppException ex)
            {
                logger.LogInformation("AI analysis skipped for {LeadId}: {Reason}", leadId, ex.Message);
                var lead = await db.Leads.FirstAsync(l => l.Id == leadId, ct);
                lead.AIAnalysisStatus = ProcessStatus.Skipped;
                await db.SaveChangesAsync(ct);
            }
        }
        await ScoreAsync(leadId, ct);
    }

    private async Task ReplaceRuleOpportunitiesAsync(Lead lead, WebsiteAnalysis? analysis, CancellationToken ct)
    {
        var old = await db.Opportunities.Where(o => o.LeadId == lead.Id && o.Source == OpportunitySource.RuleEngine).ToListAsync(ct);
        var dismissedCodes = old.Where(o => o.IsDismissed).Select(o => o.Code).ToHashSet();
        db.Opportunities.RemoveRange(old);
        foreach (var o in OpportunityDetector.Detect(lead, lead.Category, analysis))
        {
            o.IsDismissed = dismissedCodes.Contains(o.Code);
            db.Opportunities.Add(o);
        }
    }

    // ------------------------------------------------------------------ AI analysis (job)

    public async Task EnsureAIAllowedAsync(CancellationToken ct)
    {
        if (!ai.IsConfigured)
            throw new IntegrationNotConfiguredException("AI provider", "Set AI__Claude__ApiKey on the server (see docs/SETUP-CLAUDE.md).");
        if (ai.IsMock) return;
        var budget = await settings.GetAsync<BudgetSettings>(BudgetSettings.Key, ct);
        if (!budget.DisableAIWhenBudgetReached) return;
        var now = clock.UtcNow;
        var monthSpend = await usage.GetSpendAsync(ai.Name, new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), ct);
        var daySpend = await usage.GetSpendAsync(ai.Name, now.Date, ct);
        if (monthSpend >= budget.MonthlyAIBudgetUsd) throw new BudgetExceededException($"Monthly AI budget of ${budget.MonthlyAIBudgetUsd:0.00} reached. AI analysis is paused.");
        if (daySpend >= budget.DailyAIBudgetUsd) throw new BudgetExceededException($"Daily AI budget of ${budget.DailyAIBudgetUsd:0.00} reached. AI analysis resumes tomorrow.");
    }

    public async Task RunAIAnalysisAsync(Guid leadId, CancellationToken ct)
    {
        var lead = await db.Leads.Include(l => l.Category).Include(l => l.Contacts).FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return;
        var owner = await settings.GetAsync<OwnerProfile>(OwnerProfile.Key, ct);
        var website = await db.WebsiteAnalyses.AsNoTracking().Include(w => w.Evidence).Where(w => w.LeadId == leadId)
            .OrderByDescending(w => w.CreatedAt).FirstOrDefaultAsync(ct);
        var ruleOpps = await db.Opportunities.AsNoTracking().Where(o => o.LeadId == leadId && o.Source == OpportunitySource.RuleEngine && !o.IsDismissed).ToListAsync(ct);

        var prompt = BuildEvidencePrompt(lead, website, ruleOpps, owner);
        var record = new AIAnalysis
        {
            LeadId = leadId, Provider = ai.Name, Model = ai.Model, PromptVersion = PromptLibrary.LeadAnalysisVersion,
            Status = ProcessStatus.Running, InputHash = Normalizer.Sha256Hex(prompt)
        };
        db.AIAnalyses.Add(record);
        lead.AIAnalysisStatus = ProcessStatus.Running;
        await db.SaveChangesAsync(ct);

        try
        {
            await EnsureAIAllowedAsync(ct);
            var response = await ai.CompleteJsonAsync(new AIJsonRequest
            {
                Purpose = "lead-analysis", PromptVersion = PromptLibrary.LeadAnalysisVersion, SystemPrompt = PromptLibrary.LeadAnalysisSystem,
                UserPrompt = prompt, JsonSchema = AIJson.LeadAnalysisSchema, LeadId = leadId
            }, ct);
            record.InputTokens = response.InputTokens; record.OutputTokens = response.OutputTokens; record.CostUsd = response.CostUsd; record.Model = response.Model;

            var (result, errors) = AIResultValidator.ParseLeadAnalysis(response.Json, owner.Services);
            if (result is null)
            {
                record.Status = ProcessStatus.Failed;
                record.Error = "AI response failed validation: " + string.Join(" ", errors);
                record.ResultJson = response.Json;
                lead.AIAnalysisStatus = ProcessStatus.Failed;
            }
            else
            {
                record.Status = ProcessStatus.Completed;
                record.ResultJson = JsonSerializer.Serialize(result, AIJson.Options);
                record.BusinessSummary = result.BusinessSummary;
                record.SuggestedApproach = result.SuggestedApproach;
                record.RecommendedServicesJson = JsonSerializer.Serialize(result.RecommendedServices);
                record.EstimatedValueMin = result.EstimatedProjectValueMin;
                record.EstimatedValueMax = result.EstimatedProjectValueMax;
                record.ConfidenceScore = result.ConfidenceScore;
                record.NextAction = result.NextAction;
                lead.AIAnalysisStatus = ProcessStatus.Completed;
                lead.NextAction ??= result.NextAction;

                var oldAi = await db.Opportunities.Where(o => o.LeadId == leadId && o.Source == OpportunitySource.AI).ToListAsync(ct);
                db.Opportunities.RemoveRange(oldAi);
                var ruleTitles = await db.Opportunities.Where(o => o.LeadId == leadId && o.Source == OpportunitySource.RuleEngine).Select(o => o.Title).ToListAsync(ct);
                foreach (var o in result.Opportunities.Where(o => !ruleTitles.Any(t => Normalizer.Similarity(Normalizer.NormalizeName(t), Normalizer.NormalizeName(o.Title)) >= 0.9)))
                    db.Opportunities.Add(new Opportunity
                    {
                        LeadId = leadId, AIAnalysisId = record.Id, Code = "ai_" + Normalizer.Slugify(o.Title)[..Math.Min(40, Normalizer.Slugify(o.Title).Length)],
                        Title = o.Title, Problem = o.Problem, Evidence = o.Evidence, Assumptions = o.Assumptions, RecommendedService = o.RecommendedService,
                        Confidence = Enum.TryParse<Confidence>(o.Confidence, true, out var c) ? c : Confidence.Low, Source = OpportunitySource.AI
                    });
                if (lead.Stage == PipelineStage.New) lead.Stage = PipelineStage.Analysed;
                db.CrmActivities.Add(Activity.Create(leadId, ActivityType.Analysis, "AI analysis completed", $"{result.Opportunities.Count} opportunity(ies), confidence {result.ConfidenceScore}"));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "AI analysis failed for lead {LeadId}", leadId);
            record.Status = ProcessStatus.Failed;
            record.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            lead.AIAnalysisStatus = ex is BudgetExceededException ? ProcessStatus.Skipped : ProcessStatus.Failed;
        }
        await db.SaveChangesAsync(ct);
        await ScoreAsync(leadId, ct);

        if (record.Status == ProcessStatus.Completed)
            await notifications.NotifyAsync(lead.OwnerUserId, NotificationType.AnalysisCompleted, "Analysis complete",
                "A lead analysis has finished.", $"{lead.BusinessName} analysed.", $"/leads/{leadId}", ct);
    }

    public static string BuildEvidencePrompt(Lead lead, WebsiteAnalysis? w, IReadOnlyList<Opportunity> ruleOpps, OwnerProfile owner)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Business (public data, source recorded)");
        sb.AppendLine($"Name: {lead.BusinessName}");
        sb.AppendLine($"Category: {lead.Category?.Name ?? "unknown"}");
        sb.AppendLine($"Location: {string.Join(", ", new[] { lead.Locality, lead.City, lead.District, lead.State, lead.PinCode }.Where(s => !string.IsNullOrWhiteSpace(s)))}");
        sb.AppendLine($"Website: {lead.WebsiteUrl ?? "none listed"}");
        sb.AppendLine($"Public phone available: {(lead.Phone is null ? "no" : "yes")}; public email available: {(lead.Email is null ? "no" : "yes")}");
        if (lead.Rating is not null) sb.AppendLine($"Rating: {lead.Rating} from {lead.ReviewCount} reviews (provider: {lead.SourceProvider})");
        if (lead.BusinessStatus is not null) sb.AppendLine($"Listing status: {lead.BusinessStatus}");

        sb.AppendLine().AppendLine("## Website observations");
        if (w is null) sb.AppendLine("No website analysis available.");
        else if (w.Status != ProcessStatus.Completed) sb.AppendLine($"Website analysis did not complete: {w.Error}");
        else
        {
            sb.AppendLine($"Final URL: {w.FinalUrl}; HTTP {w.HttpStatusCode}; response {w.ResponseTimeMs} ms; HTTPS: {w.IsHttps}");
            sb.AppendLine($"Title: {w.Title}; Meta description: {(string.IsNullOrWhiteSpace(w.MetaDescription) ? "missing" : w.MetaDescription)}");
            sb.AppendLine($"Site type heuristic (uncertain): {w.SiteTypeGuess}");
            if (w.PageSpeedPerformance is not null) sb.AppendLine($"PageSpeed mobile: performance {w.PageSpeedPerformance}, accessibility {w.PageSpeedAccessibility}, SEO {w.PageSpeedSeo}");
            foreach (var e in w.Evidence.OrderBy(e => e.Check)) sb.AppendLine($"- [{e.Result}] {e.Check}: {e.Observation}");
        }

        sb.AppendLine().AppendLine("## Rule-based opportunities already detected (with evidence)");
        if (ruleOpps.Count == 0) sb.AppendLine("None.");
        foreach (var o in ruleOpps) sb.AppendLine($"- {o.Title} | evidence: {o.Evidence} | assumptions: {o.Assumptions ?? "none"}");

        sb.AppendLine().AppendLine("## Services the developer offers (recommend only from this list)");
        foreach (var s in owner.Services) sb.AppendLine($"- {s}");
        sb.AppendLine().AppendLine($"Typical project size the developer targets: INR {owner.TargetProjectMin:N0} to {owner.TargetProjectMax:N0}.");
        sb.AppendLine().AppendLine("Analyse this business and respond with the JSON object only.");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ scoring

    public async Task<ScoreBreakdown> ScoreAsync(Guid leadId, CancellationToken ct)
    {
        var lead = await db.Leads.Include(l => l.Category).Include(l => l.Contacts).FirstOrDefaultAsync(l => l.Id == leadId, ct)
                   ?? throw new NotFoundException("Lead", leadId);
        var weights = await settings.GetAsync<ScoringWeights>(ScoringWeights.Key, ct);
        var owner = await settings.GetAsync<OwnerProfile>(OwnerProfile.Key, ct);
        var website = await db.WebsiteAnalyses.AsNoTracking().Where(w => w.LeadId == leadId).OrderByDescending(w => w.CreatedAt).FirstOrDefaultAsync(ct);
        var opps = await db.Opportunities.AsNoTracking().Where(o => o.LeadId == leadId).ToListAsync(ct);
        var aiResult = await db.AIAnalyses.AsNoTracking().Where(a => a.LeadId == leadId && a.Status == ProcessStatus.Completed)
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);

        var breakdown = scoring.Calculate(new ScoreInput
        {
            Lead = lead, Category = lead.Category, Website = website, Opportunities = opps,
            RecommendedServices = LeadMapper.ParseList(aiResult?.RecommendedServicesJson),
            EstimatedValueMin = aiResult?.EstimatedValueMin, EstimatedValueMax = aiResult?.EstimatedValueMax
        }, weights, owner);

        var previousPriority = lead.Priority;
        lead.Score = breakdown.Total;
        lead.Priority = breakdown.Priority;
        var gaps = breakdown.Components.First(c => c.Criterion == "Website gaps");
        lead.WebsiteQualityScore = website?.Status == ProcessStatus.Completed ? (int)Math.Round(100 - gaps.Fraction * 100) : null;
        if (lead.Stage == PipelineStage.Analysed && breakdown.Total >= weights.GoodPotentialMin) lead.Stage = PipelineStage.Qualified;

        db.LeadScores.Add(new LeadScore
        {
            LeadId = leadId, Total = breakdown.Total, Priority = breakdown.Priority,
            WebsiteGaps = Points(breakdown, "Website gaps"), ServiceFit = Points(breakdown, "Service fit"),
            ContactAvailability = Points(breakdown, "Contact availability"), IdentifiedOpportunity = Points(breakdown, "Identified opportunity"),
            BusinessRelevance = Points(breakdown, "Business relevance"), BudgetSuitability = Points(breakdown, "Budget suitability"),
            WeightsJson = JsonSerializer.Serialize(weights),
            ExplanationJson = JsonSerializer.Serialize(breakdown.Components.Select(c => new ScoreComponentDto(c.Criterion, c.Weight, c.Points, c.Fraction, c.Reasons)))
        });
        await db.SaveChangesAsync(ct);

        if (breakdown.Priority == LeadPriority.High && previousPriority != LeadPriority.High)
            await notifications.NotifyAsync(lead.OwnerUserId, NotificationType.HighPriorityLeads, "High-priority lead",
                "A new high-priority lead is ready to review.", $"{lead.BusinessName} scored {breakdown.Total}.", $"/leads/{leadId}", ct);
        return breakdown;
    }

    private static int Points(ScoreBreakdown b, string criterion) => b.Components.First(c => c.Criterion == criterion).Points;

    /// <summary>Recalculates scores for all leads (after weights change).</summary>
    public async Task RescoreAllAsync(CancellationToken ct)
    {
        var ids = await db.Leads.Select(l => l.Id).ToListAsync(ct);
        foreach (var id in ids) await ScoreAsync(id, ct);
    }
}
