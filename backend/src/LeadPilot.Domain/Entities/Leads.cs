using LeadPilot.Domain.Common;

namespace LeadPilot.Domain.Entities;

public class LeadCategory : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Search keywords sent to discovery providers (e.g. "coaching institute").</summary>
    public string? SearchKeywords { get; set; }
    /// <summary>Comma separated list of services usually relevant to this category (used by scoring).</summary>
    public string? RelevantServices { get; set; }
    /// <summary>0-100 relevance of this category to the owner's services.</summary>
    public int RelevanceScore { get; set; } = 60;
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }
}

public class Lead : BaseEntity, IConcurrencyAware, ISoftDelete, IOwned
{
    public Guid OwnerUserId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public LeadCategory? Category { get; set; }

    public string? WebsiteUrl { get; set; }
    public string? WebsiteDomain { get; set; }
    /// <summary>Primary public business phone (denormalised from LeadContacts for search and dedupe).</summary>
    public string? Phone { get; set; }
    public string? NormalizedPhone { get; set; }
    public string? Email { get; set; }

    public string? AddressLine { get; set; }
    public string? Locality { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? State { get; set; }
    public string? PinCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string SourceProvider { get; set; } = "manual";
    public string? ProviderPlaceId { get; set; }
    public string? SourceUrl { get; set; }
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastVerifiedAt { get; set; }

    public double? Rating { get; set; }
    public int? ReviewCount { get; set; }
    public string? BusinessStatus { get; set; }

    public ProcessStatus ContactDiscoveryStatus { get; set; }
    public ProcessStatus WebsiteAnalysisStatus { get; set; }
    public ProcessStatus AIAnalysisStatus { get; set; }

    public int? Score { get; set; }
    /// <summary>0-100, derived from the latest website analysis (100 = no gaps observed).</summary>
    public int? WebsiteQualityScore { get; set; }
    public LeadPriority? Priority { get; set; }
    public PipelineStage Stage { get; set; } = PipelineStage.New;

    public decimal? ExpectedDealValue { get; set; }
    public decimal? ActualDealValue { get; set; }
    public string? LostReason { get; set; }
    public string? NextAction { get; set; }
    public DateTime? NextFollowUpAt { get; set; }
    public DateTime? LastContactedAt { get; set; }
    public string? Tags { get; set; }

    public Guid? DuplicateOfLeadId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<LeadContact> Contacts { get; set; } = [];
    public List<LeadSource> Sources { get; set; } = [];
    public List<Opportunity> Opportunities { get; set; } = [];
}

/// <summary>
/// Every contact detail records where it was found. Verification concepts are kept separate:
/// source (where found), syntax (format valid), delivery (confirmed delivery), consent (permission exists).
/// </summary>
public class LeadContact : BaseEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public ContactType Type { get; set; }
    public string Value { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public string? Label { get; set; }
    public bool IsPrimary { get; set; }

    public string SourceProvider { get; set; } = "manual";
    public string? SourceUrl { get; set; }
    public DateTime SourceRecordedAt { get; set; } = DateTime.UtcNow;

    public bool SourceVerified { get; set; }
    public bool SyntaxVerified { get; set; }
    public bool DeliveryVerified { get; set; }
    public DateTime? DeliveryVerifiedAt { get; set; }
    public bool ConsentVerified { get; set; }
}

/// <summary>Provenance record for each provider that returned this business.</summary>
public class LeadSource : BaseEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? SourceUrl { get; set; }
    public string? Attribution { get; set; }
    public Guid? DiscoveryJobId { get; set; }
    public DateTime RetrievedAt { get; set; } = DateTime.UtcNow;
}

public class DiscoveryJob : BaseEntity, IConcurrencyAware, IOwned
{
    public Guid OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = "google_places";
    /// <summary>Serialised DiscoveryQuery (categories, locations, filters).</summary>
    public string QueryJson { get; set; } = "{}";
    public int MaxResults { get; set; } = 50;
    public DiscoveryJobStatus Status { get; set; } = DiscoveryJobStatus.Pending;
    public int TotalSegments { get; set; }
    public int CompletedSegments { get; set; }
    /// <summary>Resume cursor: index of next segment and provider page token.</summary>
    public string? CursorJson { get; set; }
    public int FoundCount { get; set; }
    public int CreatedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public decimal ActualCostUsd { get; set; }
    public bool AutoAnalyse { get; set; }
    public string? HangfireJobId { get; set; }
    public string? Error { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class DiscoveryJobResult : BaseEntity
{
    public Guid DiscoveryJobId { get; set; }
    public Guid? LeadId { get; set; }
    public string? ExternalId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public DiscoveryResultOutcome Outcome { get; set; }
    public string? Message { get; set; }
}
