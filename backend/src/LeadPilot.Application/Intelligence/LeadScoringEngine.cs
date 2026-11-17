using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;

namespace LeadPilot.Application.Intelligence;

public sealed record ScoreInput
{
    public required Lead Lead { get; init; }
    public LeadCategory? Category { get; init; }
    public WebsiteAnalysis? Website { get; init; }
    public IReadOnlyList<Opportunity> Opportunities { get; init; } = [];
    /// <summary>Services recommended by AI (evidence only; AI never sets the score).</summary>
    public IReadOnlyList<string> RecommendedServices { get; init; } = [];
    public decimal? EstimatedValueMin { get; init; }
    public decimal? EstimatedValueMax { get; init; }
}

public sealed record ScoreComponent(string Criterion, int Weight, double Fraction, int Points, IReadOnlyList<string> Reasons);

public sealed record ScoreBreakdown(int Total, LeadPriority Priority, IReadOnlyList<ScoreComponent> Components);

public interface ILeadScoringService
{
    ScoreBreakdown Calculate(ScoreInput input, ScoringWeights weights, OwnerProfile owner);
}

/// <summary>
/// Deterministic, explainable scoring. Each criterion yields a fraction in [0,1] that is multiplied by its
/// configurable weight. Weights are normalised to 100 so a misconfigured total never produces scores &gt; 100.
/// </summary>
public sealed class LeadScoringEngine : ILeadScoringService
{
    public ScoreBreakdown Calculate(ScoreInput input, ScoringWeights weights, OwnerProfile owner)
    {
        static (string, int, double, List<string>) C(string name, int weight, (double F, List<string> R) r) => (name, weight, r.F, r.R);
        var raw = new List<(string Name, int Weight, double Fraction, List<string> Reasons)>
        {
            C("Website gaps", weights.WebsiteGaps, WebsiteGaps(input)),
            C("Service fit", weights.ServiceFit, ServiceFit(input)),
            C("Contact availability", weights.ContactAvailability, ContactAvailability(input)),
            C("Identified opportunity", weights.IdentifiedOpportunity, IdentifiedOpportunity(input)),
            C("Business relevance", weights.BusinessRelevance, BusinessRelevance(input)),
            C("Budget suitability", weights.BudgetSuitability, BudgetSuitability(input, owner)),
        };

        var totalWeight = Math.Max(1, raw.Sum(r => Math.Max(0, r.Weight)));
        var components = raw.Select(r =>
        {
            var normalisedWeight = Math.Max(0, r.Weight) * 100.0 / totalWeight;
            var fraction = Math.Clamp(r.Fraction, 0, 1);
            return new ScoreComponent(r.Name, (int)Math.Round(normalisedWeight), fraction,
                (int)Math.Round(normalisedWeight * fraction), r.Reasons);
        }).ToList();

        var total = Math.Clamp(components.Sum(c => c.Points), 0, 100);
        return new ScoreBreakdown(total, Classify(total, weights), components);
    }

    public static LeadPriority Classify(int score, ScoringWeights w) =>
        score >= w.HighPriorityMin ? LeadPriority.High :
        score >= w.GoodPotentialMin ? LeadPriority.Good :
        score >= w.NeedsReviewMin ? LeadPriority.NeedsReview : LeadPriority.Low;

    private static (double, List<string>) WebsiteGaps(ScoreInput i)
    {
        var reasons = new List<string>();
        if (string.IsNullOrWhiteSpace(i.Lead.WebsiteUrl))
        {
            reasons.Add("No website listed - strong need for a business website (verify the business is active).");
            return (0.9, reasons);
        }
        var w = i.Website;
        if (w is null || w.Status != ProcessStatus.Completed)
        {
            reasons.Add("Website not analysed yet - gaps unknown.");
            return (0.2, reasons);
        }
        if (w.IsReachable == false)
        {
            reasons.Add("Website was unreachable during analysis (may be temporary, or the business may be inactive - verify).");
            return (0.5, reasons);
        }

        double gaps = 0, checks = 0;
        void Check(bool? hasGap, string reason, double weight = 1)
        {
            if (hasGap is null) return;
            checks += weight;
            if (hasGap.Value) { gaps += weight; reasons.Add(reason); }
        }

        Check(w.IsHttps == false, "No HTTPS.");
        Check(w.HasViewportMeta == false, "No mobile viewport meta tag (likely not mobile-friendly).", 1.5);
        Check(w.HasEnquiryForm == false, "No enquiry form found on inspected pages.", 1.5);
        Check(w.HasContactPage == false, "No contact page found.");
        Check(w.HasClearCallToAction == false, "No clear call to action found.");
        Check(w.HasWhatsAppLink == false, "No WhatsApp contact link.", 0.5);
        Check(string.IsNullOrWhiteSpace(w.MetaDescription) ? true : false, "Missing meta description.", 0.5);
        Check(w.BrokenLinksCount is > 0, $"{w.BrokenLinksCount} broken internal link(s).", 0.5);
        if (w.PageSpeedPerformance is not null)
            Check(w.PageSpeedPerformance < 50, $"Low mobile performance score ({w.PageSpeedPerformance}/100).");
        if (w.LatestCopyrightYear is not null)
            Check(w.LatestCopyrightYear < DateTime.UtcNow.Year - 2, $"Copyright year {w.LatestCopyrightYear} suggests the site may not be maintained.", 0.5);

        if (checks == 0) { reasons.Add("No observable checks completed."); return (0.2, reasons); }
        if (gaps == 0) reasons.Add("No significant website gaps observed.");
        return (gaps / checks, reasons);
    }

    private static (double, List<string>) ServiceFit(ScoreInput i)
    {
        var reasons = new List<string>();
        var services = i.Opportunities.Where(o => !o.IsDismissed).Select(o => o.RecommendedService)
            .Concat(i.RecommendedServices)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var categoryServices = (i.Category?.RelevantServices ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (services.Count == 0 && categoryServices.Length == 0)
        {
            reasons.Add("No matching services identified yet.");
            return (0.1, reasons);
        }
        var fraction = Math.Min(1.0, services.Count * 0.3 + (categoryServices.Length > 0 ? 0.3 : 0));
        if (services.Count > 0) reasons.Add($"Matching services: {string.Join(", ", services.Take(4))}.");
        if (categoryServices.Length > 0) reasons.Add($"Category typically needs: {string.Join(", ", categoryServices.Take(3))}.");
        return (fraction, reasons);
    }

    private static (double, List<string>) ContactAvailability(ScoreInput i)
    {
        var reasons = new List<string>();
        double f = 0;
        var emails = i.Lead.Contacts.Where(c => c.Type == ContactType.Email && c.SyntaxVerified).ToList();
        var phones = i.Lead.Contacts.Where(c => c.Type is ContactType.Phone or ContactType.WhatsApp && c.SyntaxVerified).ToList();
        var hasEmail = emails.Count > 0 || !string.IsNullOrWhiteSpace(i.Lead.Email);
        var hasPhone = phones.Count > 0 || !string.IsNullOrWhiteSpace(i.Lead.Phone);
        if (hasEmail) { f += 0.6; reasons.Add("Public business email available (source recorded)."); }
        if (hasPhone) { f += 0.3; reasons.Add("Public business phone available."); }
        if (!string.IsNullOrWhiteSpace(i.Lead.WebsiteUrl)) { f += 0.1; reasons.Add("Website available as contact channel."); }
        if (f == 0) reasons.Add("No contact details available.");
        return (f, reasons);
    }

    private static (double, List<string>) IdentifiedOpportunity(ScoreInput i)
    {
        var reasons = new List<string>();
        var active = i.Opportunities.Where(o => !o.IsDismissed).ToList();
        if (active.Count == 0) { reasons.Add("No evidence-based opportunities yet."); return (0, reasons); }
        var weighted = active.Sum(o => o.Confidence switch { Confidence.High => 0.5, Confidence.Medium => 0.35, _ => 0.15 });
        reasons.Add($"{active.Count} opportunity(ies): {string.Join("; ", active.Take(3).Select(o => o.Title))}.");
        return (Math.Min(1, weighted), reasons);
    }

    private static (double, List<string>) BusinessRelevance(ScoreInput i)
    {
        var reasons = new List<string>();
        double f = (i.Category?.RelevanceScore ?? 50) / 100.0;
        reasons.Add(i.Category is null ? "No category assigned (neutral relevance)." : $"Category '{i.Category.Name}' relevance {i.Category.RelevanceScore}/100.");
        if (string.Equals(i.Lead.BusinessStatus, "CLOSED_PERMANENTLY", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Business is listed as permanently closed.");
            return (0, reasons);
        }
        if (i.Lead.ReviewCount is >= 20) { f = Math.Min(1, f + 0.15); reasons.Add($"Established presence ({i.Lead.ReviewCount} reviews)."); }
        return (f, reasons);
    }

    private static (double, List<string>) BudgetSuitability(ScoreInput i, OwnerProfile owner)
    {
        var reasons = new List<string>();
        if (i.EstimatedValueMin is null && i.EstimatedValueMax is null)
        {
            var proxy = 0.4;
            if (i.Lead.ReviewCount is >= 50) proxy += 0.2;
            if (!string.IsNullOrWhiteSpace(i.Lead.WebsiteUrl)) proxy += 0.1;
            reasons.Add("No project estimate yet; using indirect indicators only (not a verified budget).");
            return (proxy, reasons);
        }
        var min = i.EstimatedValueMin ?? i.EstimatedValueMax!.Value;
        var max = i.EstimatedValueMax ?? min;
        var overlap = Math.Min(max, owner.TargetProjectMax) - Math.Max(min, owner.TargetProjectMin);
        var span = Math.Max(1, max - min);
        var fraction = overlap <= 0 ? 0.2 : (double)Math.Min(1m, overlap / span + 0.3m);
        reasons.Add($"Internal estimate ₹{min:N0}–₹{max:N0} vs target ₹{owner.TargetProjectMin:N0}–₹{owner.TargetProjectMax:N0} (estimate, not a client budget).");
        return (fraction, reasons);
    }
}
