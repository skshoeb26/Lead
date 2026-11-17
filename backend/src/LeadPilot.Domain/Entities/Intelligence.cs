using LeadPilot.Domain.Common;

namespace LeadPilot.Domain.Entities;

public class WebsiteAnalysis : BaseEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public string RequestedUrl { get; set; } = string.Empty;
    public string? FinalUrl { get; set; }
    public ProcessStatus Status { get; set; } = ProcessStatus.Queued;
    public int? HttpStatusCode { get; set; }
    public bool? IsReachable { get; set; }
    public bool? IsHttps { get; set; }
    public bool? HttpsRedirect { get; set; }
    public int? ResponseTimeMs { get; set; }
    public long? PageSizeBytes { get; set; }
    public bool? HasViewportMeta { get; set; }
    public string? Title { get; set; }
    public string? MetaDescription { get; set; }
    public bool? HasH1 { get; set; }
    public bool? HasContactPage { get; set; }
    public bool? HasEnquiryForm { get; set; }
    public bool? HasBookingFeature { get; set; }
    public bool? HasWhatsAppLink { get; set; }
    public bool? HasClearCallToAction { get; set; }
    public bool? HasOnlineOrdering { get; set; }
    public string? SocialLinksJson { get; set; }
    public int? InternalLinksChecked { get; set; }
    public int? BrokenLinksCount { get; set; }
    public int? ImagesWithoutAlt { get; set; }
    public int? LatestCopyrightYear { get; set; }
    /// <summary>Heuristic only: "static-brochure", "cms", "web-application", "unknown". Never a certainty.</summary>
    public string? SiteTypeGuess { get; set; }
    public string? DetectedGenerator { get; set; }
    public int? PageSpeedPerformance { get; set; }
    public int? PageSpeedAccessibility { get; set; }
    public int? PageSpeedSeo { get; set; }
    public int? PageSpeedBestPractices { get; set; }
    public double? LargestContentfulPaintMs { get; set; }
    public string? PagesInspectedJson { get; set; }
    public string? Error { get; set; }
    public DateTime? AnalysedAt { get; set; }

    public List<WebsiteAnalysisEvidence> Evidence { get; set; } = [];
}

/// <summary>An individual observable check with its result and what was actually seen.</summary>
public class WebsiteAnalysisEvidence : BaseEntity
{
    public Guid WebsiteAnalysisId { get; set; }
    public string Check { get; set; } = string.Empty;
    public CheckResult Result { get; set; }
    public string Observation { get; set; } = string.Empty;
    public string? PageUrl { get; set; }
}

public class Opportunity : BaseEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public Guid? WebsiteAnalysisId { get; set; }
    public Guid? AIAnalysisId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Problem { get; set; } = string.Empty;
    /// <summary>Observed facts that support this opportunity.</summary>
    public string Evidence { get; set; } = string.Empty;
    /// <summary>Explicit assumptions, kept separate from observed evidence.</summary>
    public string? Assumptions { get; set; }
    public string RecommendedService { get; set; } = string.Empty;
    public Confidence Confidence { get; set; } = Confidence.Medium;
    public OpportunitySource Source { get; set; }
    public bool IsDismissed { get; set; }
}

public class AIAnalysis : BaseEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public ProcessStatus Status { get; set; } = ProcessStatus.Queued;
    public string? BusinessSummary { get; set; }
    public string? SuggestedApproach { get; set; }
    public string? RecommendedServicesJson { get; set; }
    public decimal? EstimatedValueMin { get; set; }
    public decimal? EstimatedValueMax { get; set; }
    public int? ConfidenceScore { get; set; }
    public string? NextAction { get; set; }
    /// <summary>Full validated JSON response.</summary>
    public string? ResultJson { get; set; }
    /// <summary>SHA-256 of the input evidence payload sent to the model (audit metadata, not the content).</summary>
    public string? InputHash { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public string? Error { get; set; }
}

public class LeadScore : BaseEntity
{
    public Guid LeadId { get; set; }
    public int Total { get; set; }
    public int WebsiteGaps { get; set; }
    public int ServiceFit { get; set; }
    public int ContactAvailability { get; set; }
    public int IdentifiedOpportunity { get; set; }
    public int BusinessRelevance { get; set; }
    public int BudgetSuitability { get; set; }
    public LeadPriority Priority { get; set; }
    public string WeightsJson { get; set; } = "{}";
    /// <summary>Per-criterion explanation lines (JSON array).</summary>
    public string ExplanationJson { get; set; } = "[]";
}

public class PitchDraft : BaseEntity, IOwned
{
    public Guid OwnerUserId { get; set; }
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public PitchType Type { get; set; }
    public PitchLanguage Language { get; set; }
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public Guid? AIAnalysisId { get; set; }
    public string? OpportunityIdsJson { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public bool IsEdited { get; set; }
    public bool IsArchived { get; set; }
}
