using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using LeadPilot.Application.Notifications;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using Microsoft.EntityFrameworkCore;

namespace LeadPilot.Application.Reports;

public sealed record DashboardSummary(
    int TotalLeads, int NewLeads, int HighPriorityLeads, int AnalysedLeads, int PendingApprovals, int EmailsSent, int RepliesRecorded,
    int MeetingsUpcoming, int ProposalsSent, int ProjectsWon, decimal RevenueWon, int FollowUpsDueToday, int FollowUpsOverdue,
    int OutcomeUnknownEmails, bool SendingPaused, bool EmailTestMode, int UnreadNotifications);

/// <summary>A rate with its numerator, denominator and definition so it is never shown without context.</summary>
public sealed record RateMetric(double? Value, int Numerator, int Denominator, string Definition);

public sealed record DashboardMetrics(
    DateTime From, DateTime To,
    int LeadsDiscoveredToday, int LeadsDiscoveredInRange, int LeadsDiscoveredThisMonth, int LeadsAnalysed, int QualifiedLeads, int HighPriority,
    int PitchDraftsGenerated, int EmailsApproved, int EmailsSent, int RepliesRecorded, int MeetingsScheduled, int ProposalsSent, int ProjectsWon,
    decimal TotalDealValue, decimal? AverageProjectValue, RateMetric ReplyRate, RateMetric MeetingRate, RateMetric WinRate,
    decimal DiscoveryCostUsd, decimal AICostUsd, decimal OtherCostUsd, decimal? CostPerQualifiedLeadUsd,
    IReadOnlyList<StageCount> Pipeline, IReadOnlyList<DailyPoint> LeadsPerDay);

public sealed record StageCount(PipelineStage Stage, int Count);
public sealed record DailyPoint(DateTime Date, int Count);

public sealed record UsageSummary(
    decimal AIMonthUsd, decimal AIDayUsd, decimal DiscoveryMonthUsd, decimal OtherMonthUsd, decimal MonthlyAIBudgetUsd, decimal DailyAIBudgetUsd,
    decimal MonthlyDiscoveryBudgetUsd, decimal HostingMonthUsd, long AIInputTokensMonth, long AIOutputTokensMonth, int AIRequestsMonth,
    int DiscoveryRequestsMonth, bool AIBudgetReached, bool DiscoveryBudgetReached, IReadOnlyList<ProviderUsage> ByProvider);

public sealed record ProviderUsage(string Provider, int Requests, decimal CostUsd, int Failures);

public sealed class DashboardService(IAppDbContext db, ISettingsService settings, IClock clock, IAIProvider ai,
    Microsoft.Extensions.Options.IOptions<EmailOptions> emailOptions, NotificationService notifications)
{
    public async Task<DashboardSummary> SummaryAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var todayEnd = now.Date.AddDays(1);
        var outreach = await settings.GetAsync<OutreachSettings>(OutreachSettings.Key, ct);
        return new DashboardSummary(
            await db.Leads.CountAsync(ct),
            await db.Leads.CountAsync(l => l.Stage == PipelineStage.New, ct),
            await db.Leads.CountAsync(l => l.Priority == LeadPriority.High, ct),
            await db.Leads.CountAsync(l => l.WebsiteAnalysisStatus == ProcessStatus.Completed || l.AIAnalysisStatus == ProcessStatus.Completed, ct),
            await db.EmailDrafts.CountAsync(e => e.Status == EmailStatus.PendingApproval, ct),
            await db.EmailDrafts.CountAsync(e => e.Status == EmailStatus.Sent, ct),
            await db.CrmActivities.CountAsync(a => a.Type == ActivityType.EmailReceived, ct),
            await db.Meetings.CountAsync(m => m.Status == MeetingStatus.Scheduled && m.ScheduledAt >= now, ct),
            await db.Proposals.CountAsync(p => p.Status == ProposalStatus.Sent || p.Status == ProposalStatus.Accepted || p.Status == ProposalStatus.Rejected, ct),
            await db.Leads.CountAsync(l => l.Stage == PipelineStage.Won, ct),
            await db.Leads.Where(l => l.Stage == PipelineStage.Won).SumAsync(l => l.ActualDealValue ?? 0, ct),
            await db.FollowUps.CountAsync(f => f.Status == FollowUpStatus.Pending && f.DueAt >= now.Date && f.DueAt < todayEnd, ct),
            await db.FollowUps.CountAsync(f => f.Status == FollowUpStatus.Pending && f.DueAt < now, ct),
            await db.EmailDrafts.CountAsync(e => e.Status == EmailStatus.OutcomeUnknown, ct),
            outreach.PauseAllSending,
            emailOptions.Value.TestMode,
            await notifications.UnreadCountAsync(userId, ct));
    }

    public async Task<DashboardMetrics> MetricsAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var end = to ?? now;
        var start = from ?? end.AddDays(-30);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var leadsInRange = db.Leads.Where(l => l.DiscoveredAt >= start && l.DiscoveredAt <= end);
        var sent = await db.EmailDrafts.CountAsync(e => e.SentAt >= start && e.SentAt <= end, ct);
        var contactedLeadIds = await db.EmailDrafts.Where(e => e.SentAt >= start && e.SentAt <= end).Select(e => e.LeadId).Distinct().ToListAsync(ct);
        var repliedLeads = await db.CrmActivities.Where(a => a.Type == ActivityType.EmailReceived && a.OccurredAt >= start && a.OccurredAt <= end && contactedLeadIds.Contains(a.LeadId))
            .Select(a => a.LeadId).Distinct().CountAsync(ct);
        var meetings = await db.Meetings.CountAsync(m => m.CreatedAt >= start && m.CreatedAt <= end, ct);
        var meetingLeads = await db.Meetings.Where(m => m.CreatedAt >= start && m.CreatedAt <= end && contactedLeadIds.Contains(m.LeadId)).Select(m => m.LeadId).Distinct().CountAsync(ct);
        var proposalsSent = await db.Proposals.CountAsync(p => p.SentAt >= start && p.SentAt <= end, ct);
        var wonDeals = await db.Deals.Where(d => d.Status == DealStatus.Won && d.ClosedAt >= start && d.ClosedAt <= end).ToListAsync(ct);
        var wonLeads = await db.Leads.Where(l => l.Stage == PipelineStage.Won && l.UpdatedAt >= start && l.UpdatedAt <= end).ToListAsync(ct);
        var won = Math.Max(wonDeals.Count, wonLeads.Count);
        var totalValue = wonDeals.Count > 0 ? wonDeals.Sum(d => d.ActualValue ?? 0) : wonLeads.Sum(l => l.ActualDealValue ?? 0);
        var closed = await db.Leads.CountAsync(l => (l.Stage == PipelineStage.Won || l.Stage == PipelineStage.Lost || l.Stage == PipelineStage.NotInterested) && l.UpdatedAt >= start && l.UpdatedAt <= end, ct);
        var qualified = await db.LeadScores.Where(s => s.CreatedAt >= start && s.CreatedAt <= end && s.Total >= 60).Select(s => s.LeadId).Distinct().CountAsync(ct);

        var usage = await db.UsageLogs.Where(u => u.CreatedAt >= start && u.CreatedAt <= end).GroupBy(u => u.Provider)
            .Select(g => new { Provider = g.Key, Cost = g.Sum(x => x.CostUsd) }).ToListAsync(ct);
        var discoveryCost = usage.Where(u => u.Provider is "google_places" or "mock_discovery").Sum(u => u.Cost);
        var aiCost = usage.Where(u => u.Provider == ai.Name || u.Provider == "claude").Sum(u => u.Cost);
        var other = usage.Sum(u => u.Cost) - discoveryCost - aiCost;

        var perDay = await leadsInRange.GroupBy(l => l.DiscoveredAt.Date).Select(g => new DailyPoint(g.Key, g.Count())).OrderBy(p => p.Date).ToListAsync(ct);
        var pipeline = await db.Leads.GroupBy(l => l.Stage).Select(g => new StageCount(g.Key, g.Count())).ToListAsync(ct);

        static RateMetric Rate(int n, int d, string def) => new(d == 0 ? null : Math.Round(n * 100.0 / d, 1), n, d, def);

        return new DashboardMetrics(start, end,
            await db.Leads.CountAsync(l => l.DiscoveredAt >= now.Date, ct),
            await leadsInRange.CountAsync(ct),
            await db.Leads.CountAsync(l => l.DiscoveredAt >= monthStart, ct),
            await db.WebsiteAnalyses.Where(w => w.CreatedAt >= start && w.CreatedAt <= end).Select(w => w.LeadId).Distinct().CountAsync(ct),
            qualified,
            await db.Leads.CountAsync(l => l.Priority == LeadPriority.High, ct),
            await db.PitchDrafts.CountAsync(p => p.CreatedAt >= start && p.CreatedAt <= end, ct),
            await db.EmailApprovals.CountAsync(a => a.ApprovedAt >= start && a.ApprovedAt <= end, ct),
            sent, repliedLeads, meetings, proposalsSent, won, totalValue,
            won == 0 ? null : Math.Round(totalValue / won, 0),
            Rate(repliedLeads, contactedLeadIds.Count, "Leads with a manually recorded reply ÷ distinct leads emailed in this period. Replies are only counted when recorded in the CRM, so this may under-count."),
            Rate(meetingLeads, contactedLeadIds.Count, "Leads emailed in this period that got a meeting ÷ distinct leads emailed in this period."),
            Rate(won, closed, "Won ÷ leads closed (Won + Lost + Not Interested) in this period."),
            discoveryCost, aiCost, other,
            qualified == 0 ? null : Math.Round((discoveryCost + aiCost) / qualified, 3),
            pipeline, perDay);
    }

    public async Task<UsageSummary> UsageAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var budget = await settings.GetAsync<BudgetSettings>(BudgetSettings.Key, ct);
        var month = await db.UsageLogs.Where(u => u.CreatedAt >= monthStart).ToListAsync(ct);
        var aiLogs = month.Where(u => u.Provider == ai.Name).ToList();
        var aiDay = aiLogs.Where(u => u.CreatedAt >= now.Date).Sum(u => u.CostUsd);
        var discovery = month.Where(u => u.Provider is "google_places" or "mock_discovery").ToList();
        var aiMonth = aiLogs.Sum(u => u.CostUsd);
        var discMonth = discovery.Sum(u => u.CostUsd);
        return new UsageSummary(aiMonth, aiDay, discMonth, month.Sum(u => u.CostUsd) - aiMonth - discMonth,
            budget.MonthlyAIBudgetUsd, budget.DailyAIBudgetUsd, budget.MonthlyDiscoveryBudgetUsd, budget.MonthlyHostingCostUsd,
            aiLogs.Sum(u => u.InputTokens), aiLogs.Sum(u => u.OutputTokens), aiLogs.Count, discovery.Count,
            aiMonth >= budget.MonthlyAIBudgetUsd || aiDay >= budget.DailyAIBudgetUsd, discMonth >= budget.MonthlyDiscoveryBudgetUsd,
            month.GroupBy(u => u.Provider).Select(g => new ProviderUsage(g.Key, g.Count(), g.Sum(x => x.CostUsd), g.Count(x => !x.Success))).ToList());
    }
}
