using Hangfire;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using LeadPilot.Application.Crm;
using LeadPilot.Application.Notifications;
using LeadPilot.Application.Outreach;
using LeadPilot.Application.Reports;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using LeadPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadPilot.Infrastructure.Jobs;

public sealed class MaintenanceJob(
    AppDbContext db,
    IEnumerable<IBusinessEnrichmentProvider> enrichment,
    IOptions<GooglePlacesOptions> places,
    IClock clock,
    ILogger<MaintenanceJob> logger)
{
    /// <summary>Expires stale approvals, removes old OAuth states, enforces provider content retention.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;

        var expired = await db.EmailApprovals.Where(a => a.RevokedAt == null && a.ConsumedAt == null && a.ExpiresAt < now).ToListAsync(ct);
        foreach (var a in expired)
        {
            a.RevokedAt = now; a.RevokedReason = "expired";
            var d = await db.EmailDrafts.FirstOrDefaultAsync(x => x.Id == a.EmailDraftId && x.Status == EmailStatus.Approved, ct);
            if (d is not null) { d.Status = EmailStatus.Expired; db.EmailAuditLogs.Add(new() { EmailDraftId = d.Id, Action = "approval_expired", ContentHash = d.ContentHash, DraftVersion = d.Version }); }
        }

        db.OAuthStates.RemoveRange(db.OAuthStates.Where(s => s.ExpiresAt < now.AddDays(-1)));
        db.RefreshTokens.RemoveRange(db.RefreshTokens.Where(t => t.ExpiresAt < now.AddDays(-30)));
        db.Notifications.RemoveRange(db.Notifications.Where(n => n.ReadAt != null && n.CreatedAt < now.AddDays(-90)));
        await db.SaveChangesAsync(ct);

        await EnforcePlacesRetentionAsync(ct);
    }

    /// <summary>
    /// Google Places content (other than the place ID) must not be cached beyond the period allowed by Google's terms.
    /// Leads not yet engaged have their Places-derived content refreshed (if enabled) or cleared. Data the user has
    /// verified or engaged with (e.g. contacted) is retained as the user's own CRM record - review this against current terms.
    /// </summary>
    private async Task EnforcePlacesRetentionAsync(CancellationToken ct)
    {
        var o = places.Value;
        var cutoff = clock.UtcNow.AddDays(-o.ContentRetentionDays);
        var stale = await db.Leads.Include(l => l.Contacts)
            .Where(l => l.SourceProvider == "google_places" && l.LastVerifiedAt < cutoff && l.Stage <= PipelineStage.Qualified)
            .Take(200).ToListAsync(ct);
        if (stale.Count == 0) return;
        var provider = enrichment.FirstOrDefault(p => p.Name == "google_places" && p.IsConfigured);
        foreach (var lead in stale)
        {
            try
            {
                if (o.RefreshContentOnExpiry && provider is not null && lead.ProviderPlaceId is not null)
                {
                    var fresh = await provider.GetDetailsAsync(lead.ProviderPlaceId, ct);
                    if (fresh is not null)
                    {
                        lead.Rating = fresh.Rating; lead.ReviewCount = fresh.ReviewCount; lead.BusinessStatus = fresh.BusinessStatus;
                        lead.AddressLine = fresh.FormattedAddress; lead.Latitude = fresh.Latitude; lead.Longitude = fresh.Longitude;
                        lead.LastVerifiedAt = clock.UtcNow;
                        continue;
                    }
                }
                lead.Rating = null; lead.ReviewCount = null; lead.Latitude = null; lead.Longitude = null;
                lead.LastVerifiedAt = clock.UtcNow;
                db.CrmActivities.Add(Activity.Create(lead.Id, ActivityType.System, "Provider content expired", "Cached Google Places attributes cleared per retention policy."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Retention refresh failed for lead {LeadId}", lead.Id);
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

public sealed class BudgetMonitorJob(DashboardService dashboard, NotificationService notifications, ISettingsService settings, AppDbContext db, IClock clock)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var usage = await dashboard.UsageAsync(ct);
        var budget = await settings.GetAsync<BudgetSettings>(BudgetSettings.Key, ct);
        var warnAt = budget.WarnAtPercent / 100m;
        var today = clock.UtcNow.Date;
        var alreadyWarned = await db.Notifications.AnyAsync(n => n.Type == NotificationType.BudgetWarning && n.CreatedAt >= today, ct);
        if (alreadyWarned) return;
        var messages = new List<string>();
        if (budget.MonthlyAIBudgetUsd > 0 && usage.AIMonthUsd >= budget.MonthlyAIBudgetUsd * warnAt) messages.Add($"AI: ${usage.AIMonthUsd:0.00} of ${budget.MonthlyAIBudgetUsd:0.00}");
        if (budget.MonthlyDiscoveryBudgetUsd > 0 && usage.DiscoveryMonthUsd >= budget.MonthlyDiscoveryBudgetUsd * warnAt) messages.Add($"Discovery: ${usage.DiscoveryMonthUsd:0.00} of ${budget.MonthlyDiscoveryBudgetUsd:0.00}");
        if (messages.Count > 0)
            await notifications.NotifyOwnersAsync(NotificationType.BudgetWarning, "Budget warning", "API usage is approaching your budget limit.", string.Join("; ", messages), "/settings/usage", ct);
    }
}

public static class RecurringJobs
{
    public static void Register(IRecurringJobManager manager, JobsOptions o)
    {
        manager.AddOrUpdate<EmailOutboxProcessor>("email-outbox", p => p.ProcessDueAsync(CancellationToken.None), o.OutboxCron);
        manager.AddOrUpdate<FollowUpJob>("follow-ups", j => j.RunAsync(CancellationToken.None), o.FollowUpCron);
        manager.AddOrUpdate<MaintenanceJob>("maintenance", j => j.RunAsync(CancellationToken.None), o.MaintenanceCron);
        manager.AddOrUpdate<BudgetMonitorJob>("budget-monitor", j => j.RunAsync(CancellationToken.None), o.BudgetCheckCron);
    }
}
