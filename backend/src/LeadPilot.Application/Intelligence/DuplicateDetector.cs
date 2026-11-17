using LeadPilot.Application.Common;
using LeadPilot.Domain.Entities;

namespace LeadPilot.Application.Intelligence;

public sealed record DuplicateCandidate(Guid LeadId, string BusinessName, double Score, bool IsExact, IReadOnlyList<string> Reasons);

public sealed record DuplicateProbe(
    string? ProviderName,
    string? ProviderPlaceId,
    string BusinessName,
    string? WebsiteUrl,
    string? Phone,
    string? City,
    string? PinCode);

/// <summary>
/// Pure duplicate matching. Exact signals: provider id, website domain, normalised phone.
/// Fuzzy signal: normalised name similarity combined with same city / PIN code.
/// </summary>
public static class DuplicateDetector
{
    public const double FuzzyThreshold = 0.88;

    /// <summary>Shared hosting/listing domains that must never be treated as a unique business identity.</summary>
    private static readonly HashSet<string> SharedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "facebook.com", "instagram.com", "linkedin.com", "justdial.com", "indiamart.com", "google.com",
        "sites.google.com", "business.site", "wixsite.com", "blogspot.com", "wordpress.com", "linktr.ee", "youtube.com",
        "twitter.com", "x.com", "wa.me", "api.whatsapp.com"
    };

    public static bool IsSharedDomain(string? domain) =>
        domain is not null && (SharedDomains.Contains(domain) || SharedDomains.Any(d => domain.EndsWith("." + d)));

    public static DuplicateCandidate? Compare(DuplicateProbe probe, Lead existing)
    {
        var reasons = new List<string>();
        var exact = false;
        double score = 0;

        if (!string.IsNullOrEmpty(probe.ProviderPlaceId) && probe.ProviderPlaceId == existing.ProviderPlaceId
            && string.Equals(probe.ProviderName, existing.SourceProvider, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Same provider identifier");
            exact = true;
            score = 1;
        }

        var domain = Normalizer.ExtractDomain(probe.WebsiteUrl);
        if (domain is not null && !IsSharedDomain(domain) && domain == existing.WebsiteDomain)
        {
            reasons.Add($"Same website domain ({domain})");
            exact = true;
            score = 1;
        }

        var phone = Normalizer.NormalizePhone(probe.Phone);
        if (phone is not null && phone.Length >= 10 && phone == existing.NormalizedPhone)
        {
            reasons.Add("Same phone number");
            exact = true;
            score = 1;
        }

        var name = Normalizer.NormalizeName(probe.BusinessName);
        if (name.Length > 0 && existing.NormalizedName.Length > 0)
        {
            var similarity = Normalizer.Similarity(name, existing.NormalizedName);
            var sameArea = (!string.IsNullOrEmpty(probe.PinCode) && probe.PinCode == existing.PinCode)
                           || (!string.IsNullOrEmpty(probe.City) && string.Equals(probe.City, existing.City, StringComparison.OrdinalIgnoreCase));
            if (similarity >= FuzzyThreshold && sameArea)
            {
                reasons.Add($"Similar name ({similarity:P0}) in the same area");
                score = Math.Max(score, similarity * 0.95);
            }
        }

        return reasons.Count == 0 ? null : new DuplicateCandidate(existing.Id, existing.BusinessName, Math.Round(score, 3), exact, reasons);
    }
}
