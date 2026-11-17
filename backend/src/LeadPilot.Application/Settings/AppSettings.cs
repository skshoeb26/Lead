namespace LeadPilot.Application.Settings;

// ---------------------------------------------------------------------------
// Runtime settings stored in the SystemSettings table and editable from the app.
// Every section has safe defaults.
// ---------------------------------------------------------------------------

public sealed record ScoringWeights
{
    public const string Key = "scoring.weights";
    public int WebsiteGaps { get; init; } = 25;
    public int ServiceFit { get; init; } = 20;
    public int ContactAvailability { get; init; } = 15;
    public int IdentifiedOpportunity { get; init; } = 15;
    public int BusinessRelevance { get; init; } = 15;
    public int BudgetSuitability { get; init; } = 10;
    public int HighPriorityMin { get; init; } = 80;
    public int GoodPotentialMin { get; init; } = 60;
    public int NeedsReviewMin { get; init; } = 40;

    public int Total => WebsiteGaps + ServiceFit + ContactAvailability + IdentifiedOpportunity + BusinessRelevance + BudgetSuitability;
}

public sealed record OutreachSettings
{
    public const string Key = "outreach";
    /// <summary>Global kill switch: nothing is sent while true.</summary>
    public bool PauseAllSending { get; init; }
    public int DailySendLimit { get; init; } = 20;
    public int HourlySendLimit { get; init; } = 6;
    public int ApprovalValidityHours { get; init; } = 72;
    /// <summary>Recipient must exist as a source-recorded contact on the lead.</summary>
    public bool RequireSourceRecordedRecipient { get; init; } = true;
    /// <summary>Require a recorded consent before email is sent. Off by default for B2B email to published business addresses; review your own legal position.</summary>
    public bool RequireEmailConsent { get; init; }
    public bool AppendOptOutFooter { get; init; } = true;
    public string OptOutFooter { get; init; } = "If you'd prefer not to hear from me again, just reply \"unsubscribe\" and I won't contact you further.";
    public int[] FollowUpDays { get; init; } = [3, 7, 14];
    public bool AutoCreateFollowUps { get; init; } = true;
    /// <summary>Automatically generate follow-up drafts into Pending Approval when a follow-up becomes due. Never auto-sends.</summary>
    public bool AutoDraftFollowUps { get; init; } = true;
}

public sealed record OwnerProfile
{
    public const string Key = "owner.profile";
    public string Name { get; init; } = "";
    public string BusinessName { get; init; } = "";
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? PortfolioUrl { get; init; }
    public string? City { get; init; }
    public string Signature { get; init; } = "";
    public decimal TargetProjectMin { get; init; } = 10000;
    public decimal TargetProjectMax { get; init; } = 50000;
    public string[] Services { get; init; } =
    [
        "Business websites and website redesign",
        "Custom web application development",
        "CRM and ERP development",
        "Business process automation",
        "Android mobile application development",
        "AI chatbot development",
        "AI-powered business automation",
        "API integration",
        "Dashboard and reporting solutions",
        "Maintenance and technical consulting"
    ];
}

public sealed record BudgetSettings
{
    public const string Key = "budget";
    public decimal MonthlyAIBudgetUsd { get; init; } = 10m;
    public decimal DailyAIBudgetUsd { get; init; } = 2m;
    public decimal MonthlyDiscoveryBudgetUsd { get; init; } = 20m;
    public bool DisableAIWhenBudgetReached { get; init; } = true;
    public bool BlockDiscoveryWhenBudgetReached { get; init; } = true;
    public int WarnAtPercent { get; init; } = 80;
    /// <summary>Discovery jobs above this estimated cost require explicit confirmation.</summary>
    public decimal ConfirmDiscoveryAboveUsd { get; init; } = 1m;
    public decimal MonthlyHostingCostUsd { get; init; } = 6m;
}

public sealed record AutomationSettings
{
    public const string Key = "automation";
    /// <summary>Default: discovery only imports and stores. Analysis runs on selected leads.</summary>
    public bool AutoWebsiteAnalysisAfterDiscovery { get; init; }
    public bool AutoAIAnalysisAfterWebsiteAnalysis { get; init; }
    public bool AutoScoreAfterAnalysis { get; init; } = true;
    public bool UsePageSpeed { get; init; } = true;
    public string DefaultPitchLanguage { get; init; } = "English";
}

public sealed record NotificationSettings
{
    public const string Key = "notifications";
    public bool PushEnabled { get; init; } = true;
    /// <summary>When false (default), push previews never contain lead names or email content.</summary>
    public bool IncludeDetailsInPush { get; init; }
    public bool DiscoveryCompleted { get; init; } = true;
    public bool HighPriorityLeads { get; init; } = true;
    public bool AnalysisCompleted { get; init; } = true;
    public bool ApprovalPending { get; init; } = true;
    public bool EmailFailed { get; init; } = true;
    public bool FollowUpDue { get; init; } = true;
    public bool JobErrors { get; init; } = true;
}

public interface ISettingsService
{
    Task<T> GetAsync<T>(string key, CancellationToken ct = default) where T : new();
    Task SaveAsync<T>(string key, T value, CancellationToken ct = default);
}
