using System.Text.Json;
using LeadPilot.Application.Intelligence;
using LeadPilot.Domain.Entities;

namespace LeadPilot.Application.Leads;

public static class LeadMapper
{
    public static List<string> ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    public static OpportunityDto ToDto(this Opportunity o) =>
        new(o.Id, o.Code, o.Title, o.Problem, o.Evidence, o.Assumptions, o.RecommendedService, o.Confidence, o.Source, o.IsDismissed, o.CreatedAt);

    public static ScoreDto ToDto(this LeadScore s)
    {
        var components = JsonSerializer.Deserialize<List<ScoreComponentDto>>(s.ExplanationJson) ?? [];
        return new ScoreDto(s.Total, s.Priority, s.CreatedAt, components);
    }

    public static WebsiteAnalysisDto ToDto(this WebsiteAnalysis w) => new(
        w.Id, w.Status, w.RequestedUrl, w.FinalUrl, w.HttpStatusCode, w.IsReachable, w.IsHttps, w.ResponseTimeMs, w.PageSizeBytes,
        w.HasViewportMeta, w.Title, w.MetaDescription, w.HasContactPage, w.HasEnquiryForm, w.HasBookingFeature, w.HasWhatsAppLink,
        w.HasClearCallToAction, ParseList(w.SocialLinksJson), w.BrokenLinksCount, w.InternalLinksChecked, w.LatestCopyrightYear,
        w.SiteTypeGuess, w.PageSpeedPerformance, w.PageSpeedAccessibility, w.PageSpeedSeo, w.PageSpeedBestPractices,
        w.LargestContentfulPaintMs, ParseList(w.PagesInspectedJson), w.Error, w.AnalysedAt,
        w.Evidence.OrderBy(e => e.Check).Select(e => new EvidenceDto(e.Check, e.Result, e.Observation, e.PageUrl)).ToList());

    public static AIAnalysisDto ToDto(this AIAnalysis a)
    {
        List<string> unknowns = [];
        string? quality = null;
        if (a.ResultJson is not null)
        {
            try
            {
                var r = JsonSerializer.Deserialize<LeadAnalysisResult>(a.ResultJson, AIJson.Options);
                unknowns = r?.Unknowns ?? [];
                quality = r?.EvidenceQuality;
            }
            catch (JsonException) { }
        }
        return new AIAnalysisDto(a.Id, a.Status, a.Provider, a.Model, a.PromptVersion, a.BusinessSummary, a.SuggestedApproach,
            ParseList(a.RecommendedServicesJson), a.EstimatedValueMin, a.EstimatedValueMax, a.ConfidenceScore, a.NextAction,
            unknowns, quality, a.InputTokens, a.OutputTokens, a.CostUsd, a.Error, a.CreatedAt, a.Provider == "mock");
    }
}
