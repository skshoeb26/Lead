using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Analysis;
using LeadPilot.Application.Common;
using LeadPilot.Application.Settings;
using LeadPilot.Infrastructure.Gmail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LeadPilot.API.Controllers;

public sealed record AllSettings(ScoringWeights Scoring, OutreachSettings Outreach, OwnerProfile Owner, BudgetSettings Budget, AutomationSettings Automation, NotificationSettings Notifications);

public sealed record IntegrationStatus(string Key, string Name, bool Configured, bool IsMock, string? Detail);

public sealed record SystemStatus(string Environment, bool EmailTestMode, bool JobsEnabled, IReadOnlyList<IntegrationStatus> Integrations, string Version);

[ApiController]
[Route("api/v1/settings")]
[Produces("application/json")]
[Authorize(Policy = "Owner")]
public sealed class SettingsController(
    ISettingsService settings,
    IAuditService audit,
    IJobScheduler jobs,
    IEnumerable<ILeadDiscoveryProvider> discovery,
    IAIProvider ai,
    IPageSpeedClient pageSpeed,
    INotificationProvider push,
    IWhatsAppProvider whatsApp,
    GoogleOAuthService google,
    IOptions<EmailOptions> email,
    IOptions<JobsOptions> jobsOptions,
    IHostEnvironment env) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AllSettings>> Get(CancellationToken ct) => Ok(new AllSettings(
        await settings.GetAsync<ScoringWeights>(ScoringWeights.Key, ct),
        await settings.GetAsync<OutreachSettings>(OutreachSettings.Key, ct),
        await settings.GetAsync<OwnerProfile>(OwnerProfile.Key, ct),
        await settings.GetAsync<BudgetSettings>(BudgetSettings.Key, ct),
        await settings.GetAsync<AutomationSettings>(AutomationSettings.Key, ct),
        await settings.GetAsync<NotificationSettings>(NotificationSettings.Key, ct)));

    [HttpPut("scoring")]
    public async Task<IActionResult> Scoring(ScoringWeights value, CancellationToken ct)
    {
        var parts = new[] { value.WebsiteGaps, value.ServiceFit, value.ContactAvailability, value.IdentifiedOpportunity, value.BusinessRelevance, value.BudgetSuitability };
        if (parts.Any(p => p is < 0 or > 100)) throw new ValidationFailedException("Each weight must be between 0 and 100.");
        if (value.Total == 0) throw new ValidationFailedException("At least one weight must be greater than zero.");
        if (!(value.HighPriorityMin > value.GoodPotentialMin && value.GoodPotentialMin > value.NeedsReviewMin && value.NeedsReviewMin >= 0 && value.HighPriorityMin <= 100))
            throw new ValidationFailedException("Priority thresholds must be descending: high > good > review ≥ 0.");
        await settings.SaveAsync(ScoringWeights.Key, value, ct);
        await audit.LogAsync("settings.scoring", details: System.Text.Json.JsonSerializer.Serialize(value), ct: ct);
        jobs.Enqueue<LeadAnalysisPipeline>(p => p.RescoreAllAsync(CancellationToken.None));
        return NoContent();
    }

    [HttpPut("outreach")]
    public async Task<IActionResult> Outreach(OutreachSettings value, CancellationToken ct)
    {
        if (value.DailySendLimit is < 0 or > 500 || value.HourlySendLimit is < 0 or > 100) throw new ValidationFailedException("Send limits out of range (daily ≤ 500, hourly ≤ 100).");
        if (value.ApprovalValidityHours is < 1 or > 720) throw new ValidationFailedException("Approval validity must be 1-720 hours.");
        if (value.FollowUpDays.Length > 10 || value.FollowUpDays.Any(d => d is < 1 or > 365)) throw new ValidationFailedException("Follow-up days must be 1-365 (max 10 steps).");
        await settings.SaveAsync(OutreachSettings.Key, value, ct);
        await audit.LogAsync(value.PauseAllSending ? "settings.outreach.paused" : "settings.outreach", details: $"daily={value.DailySendLimit} hourly={value.HourlySendLimit}", ct: ct);
        return NoContent();
    }

    /// <summary>Global kill switch for all outgoing messages.</summary>
    [HttpPost("outreach/pause")]
    public async Task<IActionResult> Pause([FromQuery] bool paused = true, CancellationToken ct = default)
    {
        var current = await settings.GetAsync<OutreachSettings>(OutreachSettings.Key, ct);
        await settings.SaveAsync(OutreachSettings.Key, current with { PauseAllSending = paused }, ct);
        await audit.LogAsync(paused ? "outreach.paused" : "outreach.resumed", ct: ct);
        return NoContent();
    }

    [HttpPut("owner")]
    public async Task<IActionResult> Owner(OwnerProfile value, CancellationToken ct)
    {
        if (value.TargetProjectMin < 0 || value.TargetProjectMax < value.TargetProjectMin) throw new ValidationFailedException("Target project range is invalid.");
        if (value.Services.Length == 0) throw new ValidationFailedException("List at least one service.");
        if (value.Email is not null && !Normalizer.IsValidEmailSyntax(value.Email)) throw new ValidationFailedException("Email is invalid.");
        await settings.SaveAsync(OwnerProfile.Key, value, ct);
        return NoContent();
    }

    [HttpPut("budget")]
    public async Task<IActionResult> Budget(BudgetSettings value, CancellationToken ct)
    {
        if (value.MonthlyAIBudgetUsd < 0 || value.DailyAIBudgetUsd < 0 || value.MonthlyDiscoveryBudgetUsd < 0) throw new ValidationFailedException("Budgets cannot be negative.");
        if (value.WarnAtPercent is < 1 or > 100) throw new ValidationFailedException("Warning threshold must be 1-100%.");
        await settings.SaveAsync(BudgetSettings.Key, value, ct);
        await audit.LogAsync("settings.budget", details: $"ai={value.MonthlyAIBudgetUsd} discovery={value.MonthlyDiscoveryBudgetUsd}", ct: ct);
        return NoContent();
    }

    [HttpPut("automation")]
    public async Task<IActionResult> Automation(AutomationSettings value, CancellationToken ct)
    {
        await settings.SaveAsync(AutomationSettings.Key, value, ct);
        return NoContent();
    }

    [HttpPut("notifications")]
    public async Task<IActionResult> Notifications(NotificationSettings value, CancellationToken ct)
    {
        await settings.SaveAsync(NotificationSettings.Key, value, ct);
        return NoContent();
    }

    [HttpGet("status"), Authorize]
    public ActionResult<SystemStatus> Status()
    {
        var list = discovery.Select(d => new IntegrationStatus(d.Name, d.DisplayName, d.IsConfigured, d.IsMock, d.IsMock ? "Fictitious demo data — development only" : null)).ToList();
        list.Add(new IntegrationStatus("ai", $"AI ({ai.Name}: {ai.Model})", ai.IsConfigured, ai.IsMock, ai.IsMock ? "Mock AI — development only" : null));
        list.Add(new IntegrationStatus("pagespeed", "PageSpeed Insights", pageSpeed.IsConfigured, false, null));
        list.Add(new IntegrationStatus("gmail", "Gmail OAuth", google.IsConfigured, false, email.Value.TestMode ? "Test mode: no real emails are sent" : null));
        list.Add(new IntegrationStatus("fcm", "Firebase Cloud Messaging", push.IsConfigured, false, null));
        list.Add(new IntegrationStatus("whatsapp", "WhatsApp Cloud API", whatsApp.IsConfigured, false, "Drafts only until opt-in workflow and business account are configured"));
        return Ok(new SystemStatus(env.EnvironmentName, email.Value.TestMode, jobsOptions.Value.Enabled, list,
            typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"));
    }
}
