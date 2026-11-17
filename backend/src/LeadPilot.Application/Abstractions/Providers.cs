using LeadPilot.Domain;
using LeadPilot.Domain.Entities;

namespace LeadPilot.Application.Abstractions;

// ============================ Discovery ============================

public sealed record DiscoverySearchRequest(
    string Keyword,
    string? Locality,
    string? City,
    string? District,
    string? State,
    string? PinCode,
    int PageSize,
    string? PageToken);

public sealed record DiscoveredBusiness
{
    public required string ExternalId { get; init; }
    public required string Name { get; init; }
    public string? WebsiteUrl { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? FormattedAddress { get; init; }
    public string? Locality { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }
    public string? State { get; init; }
    public string? PinCode { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? Rating { get; init; }
    public int? ReviewCount { get; init; }
    public string? BusinessStatus { get; init; }
    public string? SourceUrl { get; init; }
    public IReadOnlyList<string> Types { get; init; } = [];
}

public sealed record DiscoverySearchPage(IReadOnlyList<DiscoveredBusiness> Results, string? NextPageToken, decimal CostUsd);

public interface ILeadDiscoveryProvider
{
    string Name { get; }
    string DisplayName { get; }
    bool IsConfigured { get; }
    bool IsMock { get; }
    string Attribution { get; }
    decimal EstimatedCostPerRequestUsd { get; }
    int MaxResultsPerQuery { get; }
    Task<DiscoverySearchPage> SearchAsync(DiscoverySearchRequest request, CancellationToken ct);
}

/// <summary>Refreshes or enriches a known business from an approved provider (e.g. Place Details by id).</summary>
public interface IBusinessEnrichmentProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<DiscoveredBusiness?> GetDetailsAsync(string externalId, CancellationToken ct);
}

// ============================ Website analysis ============================

public interface IUrlSafetyValidator
{
    /// <summary>Validates scheme/port/host syntax. Throws <see cref="UnsafeUrlException"/> if unsafe.</summary>
    Uri ValidateUrl(string url);
    bool IsPublicAddress(System.Net.IPAddress address);
}

public sealed class UnsafeUrlException(string message) : Exception(message);

public sealed record WebsiteAnalysisResult(WebsiteAnalysis Analysis, IReadOnlyList<WebsiteAnalysisEvidence> Evidence);

public interface IWebsiteAnalyzer
{
    Task<WebsiteAnalysisResult> AnalyseAsync(Guid leadId, string url, bool usePageSpeed, CancellationToken ct);
}

public sealed record PageSpeedResult(int? Performance, int? Accessibility, int? Seo, int? BestPractices, double? LcpMs);

public interface IPageSpeedClient
{
    bool IsConfigured { get; }
    Task<PageSpeedResult?> RunAsync(string url, CancellationToken ct);
}

// ============================ AI ============================

public sealed record AIJsonRequest
{
    public required string Purpose { get; init; }
    public required string PromptVersion { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }
    /// <summary>JSON schema the response must follow.</summary>
    public required string JsonSchema { get; init; }
    public int? MaxTokens { get; init; }
    public Guid? LeadId { get; init; }
}

public sealed record AIJsonResponse(string Json, string Provider, string Model, long InputTokens, long OutputTokens, decimal CostUsd, int DurationMs);

public interface IAIProvider
{
    string Name { get; }
    string Model { get; }
    bool IsConfigured { get; }
    bool IsMock { get; }
    Task<AIJsonResponse> CompleteJsonAsync(AIJsonRequest request, CancellationToken ct);
}

// ============================ Messaging ============================

public sealed record OutgoingEmail(string From, string To, string? ToName, string Subject, string Body, string? ReplyTo, string? ListUnsubscribe);

public sealed record EmailSendResult(bool Accepted, string? MessageId, string? ThreadId, int? HttpStatus, string? Error, bool OutcomeUnknown, bool Retryable);

public interface IEmailProvider
{
    string Name { get; }
    Task<EmailSendResult> SendAsync(EmailAccount account, OutgoingEmail email, CancellationToken ct);
}

public sealed record WhatsAppSendResult(bool Accepted, string? MessageId, string? Error);

/// <summary>Official WhatsApp Business Platform (Cloud API) only. Business-initiated messages require recorded opt-in and approved templates.</summary>
public interface IWhatsAppProvider
{
    bool IsConfigured { get; }
    Task<WhatsAppSendResult> SendTemplateAsync(string toPhoneE164, string templateName, string languageCode, IReadOnlyList<string> parameters, CancellationToken ct);
}

public sealed record PushMessage(string Title, string Body, IReadOnlyDictionary<string, string> Data);

public interface INotificationProvider
{
    bool IsConfigured { get; }
    /// <summary>Returns tokens that are no longer valid and should be deactivated.</summary>
    Task<IReadOnlyList<string>> SendAsync(IReadOnlyList<string> deviceTokens, PushMessage message, CancellationToken ct);
}

// ============================ Compliance ============================

public sealed record EligibilityResult(bool Allowed, IReadOnlyList<string> Reasons)
{
    public static EligibilityResult Ok() => new(true, []);
}

public interface IConsentService
{
    Task<bool> IsSuppressedAsync(CommunicationChannel channel, string value, CancellationToken ct);
    Task<bool> HasConsentAsync(CommunicationChannel channel, string value, string? purpose, CancellationToken ct);
    Task<EligibilityResult> CheckEligibilityAsync(Lead lead, CommunicationChannel channel, string recipient, CancellationToken ct);
}
