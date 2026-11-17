using LeadPilot.Domain;

namespace LeadPilot.Application.Leads;

public sealed record LeadQuery
{
    public string? Search { get; init; }
    public Guid? CategoryId { get; init; }
    public string? State { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public string? Locality { get; init; }
    public string? PinCode { get; init; }
    public PipelineStage? Stage { get; init; }
    public LeadPriority? Priority { get; init; }
    public int? MinScore { get; init; }
    public int? MaxScore { get; init; }
    public bool? HasWebsite { get; init; }
    public bool? HasPhone { get; init; }
    public bool? HasEmail { get; init; }
    public double? MinRating { get; init; }
    public int? MaxWebsiteQuality { get; init; }
    public DateTime? DiscoveredFrom { get; init; }
    public DateTime? DiscoveredTo { get; init; }
    public string? Source { get; init; }
    /// <summary>score | name | discovered | followup | updated (prefix with '-' for descending).</summary>
    public string? Sort { get; init; } = "-discovered";
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record LeadListItem(
    Guid Id, string BusinessName, Guid? CategoryId, string? CategoryName, string? City, string? State, string? PinCode,
    string? WebsiteUrl, string? Phone, string? Email, int? Score, LeadPriority? Priority, int? WebsiteQualityScore,
    PipelineStage Stage, ProcessStatus WebsiteAnalysisStatus, ProcessStatus AIAnalysisStatus, double? Rating, int? ReviewCount,
    string SourceProvider, DateTime DiscoveredAt, DateTime? NextFollowUpAt, DateTime UpdatedAt);

public sealed record ContactDto(
    Guid Id, ContactType Type, string Value, string? Label, bool IsPrimary, string SourceProvider, string? SourceUrl,
    DateTime SourceRecordedAt, bool SourceVerified, bool SyntaxVerified, bool DeliveryVerified, bool ConsentVerified, bool Suppressed);

public sealed record SourceDto(string Provider, string? ExternalId, string? SourceUrl, string? Attribution, DateTime RetrievedAt);

public sealed record OpportunityDto(
    Guid Id, string Code, string Title, string Problem, string Evidence, string? Assumptions, string RecommendedService,
    Confidence Confidence, OpportunitySource Source, bool IsDismissed, DateTime CreatedAt);

public sealed record ScoreComponentDto(string Criterion, int Weight, int Points, double Fraction, IReadOnlyList<string> Reasons);
public sealed record ScoreDto(int Total, LeadPriority Priority, DateTime CalculatedAt, IReadOnlyList<ScoreComponentDto> Components);

public sealed record EvidenceDto(string Check, CheckResult Result, string Observation, string? PageUrl);

public sealed record WebsiteAnalysisDto(
    Guid Id, ProcessStatus Status, string RequestedUrl, string? FinalUrl, int? HttpStatusCode, bool? IsReachable, bool? IsHttps,
    int? ResponseTimeMs, long? PageSizeBytes, bool? HasViewportMeta, string? Title, string? MetaDescription, bool? HasContactPage,
    bool? HasEnquiryForm, bool? HasBookingFeature, bool? HasWhatsAppLink, bool? HasClearCallToAction, IReadOnlyList<string> SocialLinks,
    int? BrokenLinksCount, int? InternalLinksChecked, int? LatestCopyrightYear, string? SiteTypeGuess, int? PageSpeedPerformance,
    int? PageSpeedAccessibility, int? PageSpeedSeo, int? PageSpeedBestPractices, double? LargestContentfulPaintMs,
    IReadOnlyList<string> PagesInspected, string? Error, DateTime? AnalysedAt, IReadOnlyList<EvidenceDto> Evidence);

public sealed record AIAnalysisDto(
    Guid Id, ProcessStatus Status, string Provider, string Model, string PromptVersion, string? BusinessSummary,
    string? SuggestedApproach, IReadOnlyList<string> RecommendedServices, decimal? EstimatedValueMin, decimal? EstimatedValueMax,
    int? ConfidenceScore, string? NextAction, IReadOnlyList<string> Unknowns, string? EvidenceQuality, long InputTokens, long OutputTokens,
    decimal CostUsd, string? Error, DateTime CreatedAt, bool IsMock);

public sealed record LeadDetail
{
    public required Guid Id { get; init; }
    public required string BusinessName { get; init; }
    public Guid? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public string? WebsiteUrl { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? AddressLine { get; init; }
    public string? Locality { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public string? State { get; init; }
    public string? PinCode { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string SourceProvider { get; init; } = "";
    public string? ProviderPlaceId { get; init; }
    public string? SourceUrl { get; init; }
    public DateTime DiscoveredAt { get; init; }
    public DateTime? LastVerifiedAt { get; init; }
    public double? Rating { get; init; }
    public int? ReviewCount { get; init; }
    public string? BusinessStatus { get; init; }
    public int? Score { get; init; }
    public LeadPriority? Priority { get; init; }
    public int? WebsiteQualityScore { get; init; }
    public PipelineStage Stage { get; init; }
    public ProcessStatus WebsiteAnalysisStatus { get; init; }
    public ProcessStatus AIAnalysisStatus { get; init; }
    public decimal? ExpectedDealValue { get; init; }
    public decimal? ActualDealValue { get; init; }
    public string? LostReason { get; init; }
    public string? NextAction { get; init; }
    public DateTime? NextFollowUpAt { get; init; }
    public DateTime? LastContactedAt { get; init; }
    public string? Tags { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public IReadOnlyList<ContactDto> Contacts { get; init; } = [];
    public IReadOnlyList<SourceDto> Sources { get; init; } = [];
    public IReadOnlyList<OpportunityDto> Opportunities { get; init; } = [];
    public ScoreDto? LatestScore { get; init; }
    public WebsiteAnalysisDto? LatestWebsiteAnalysis { get; init; }
    public AIAnalysisDto? LatestAIAnalysis { get; init; }
}

public sealed record CreateLeadRequest
{
    public required string BusinessName { get; init; }
    public Guid? CategoryId { get; init; }
    public string? WebsiteUrl { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    /// <summary>Where the contact details were found. Required when phone/email is supplied.</summary>
    public string? ContactSourceUrl { get; init; }
    public string? AddressLine { get; init; }
    public string? Locality { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public string? State { get; init; }
    public string? PinCode { get; init; }
    public string? Tags { get; init; }
    public string? Notes { get; init; }
    public bool AllowDuplicate { get; init; }
}

public sealed record UpdateLeadRequest
{
    public required string BusinessName { get; init; }
    public Guid? CategoryId { get; init; }
    public string? WebsiteUrl { get; init; }
    public string? AddressLine { get; init; }
    public string? Locality { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public string? State { get; init; }
    public string? PinCode { get; init; }
    public string? Tags { get; init; }
    public string? NextAction { get; init; }
    public decimal? ExpectedDealValue { get; init; }
    public decimal? ActualDealValue { get; init; }
    public string? LostReason { get; init; }
    public Guid? ConcurrencyStamp { get; init; }
}

public sealed record AddContactRequest(ContactType Type, string Value, string? Label, string SourceUrl, bool IsPrimary);
public sealed record MergeLeadRequest(Guid DuplicateLeadId);
public sealed record DuplicatePairDto(Guid LeadId, string BusinessName, Guid DuplicateLeadId, string DuplicateBusinessName, double Score, bool IsExact, IReadOnlyList<string> Reasons);

public sealed record CategoryDto(Guid Id, string Name, string Slug, string? Description, string? SearchKeywords, string? RelevantServices, int RelevanceScore, bool IsActive, bool IsSystem);
public sealed record UpsertCategoryRequest(string Name, string? Description, string? SearchKeywords, string? RelevantServices, int RelevanceScore = 60, bool IsActive = true);

public sealed record CsvImportResult(int TotalRows, int Created, int Duplicates, int Skipped, IReadOnlyList<string> Errors);
