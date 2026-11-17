using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeadPilot.Application.Intelligence;

public sealed class AIOpportunity
{
    public string Title { get; set; } = "";
    public string Problem { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string? Assumptions { get; set; }
    public string RecommendedService { get; set; } = "";
    public string Confidence { get; set; } = "low";
}

public sealed class LeadAnalysisResult
{
    public string BusinessSummary { get; set; } = "";
    public List<AIOpportunity> Opportunities { get; set; } = [];
    public List<string> RecommendedServices { get; set; } = [];
    public string SuggestedApproach { get; set; } = "";
    public decimal? EstimatedProjectValueMin { get; set; }
    public decimal? EstimatedProjectValueMax { get; set; }
    public int ConfidenceScore { get; set; }
    public string EvidenceQuality { get; set; } = "low";
    public string NextAction { get; set; } = "";
    public List<string> Unknowns { get; set; } = [];
}

public sealed class PitchResult
{
    public string? Subject { get; set; }
    public string Body { get; set; } = "";
    public List<string> OpportunitiesReferenced { get; set; } = [];
}

public static class AIJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public const string LeadAnalysisSchema = """
    {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "businessSummary": { "type": "string" },
        "opportunities": {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "title": { "type": "string" },
              "problem": { "type": "string" },
              "evidence": { "type": "string" },
              "assumptions": { "type": ["string", "null"] },
              "recommendedService": { "type": "string" },
              "confidence": { "type": "string", "enum": ["low", "medium", "high"] }
            },
            "required": ["title", "problem", "evidence", "assumptions", "recommendedService", "confidence"]
          }
        },
        "recommendedServices": { "type": "array", "items": { "type": "string" } },
        "suggestedApproach": { "type": "string" },
        "estimatedProjectValueMin": { "type": ["number", "null"] },
        "estimatedProjectValueMax": { "type": ["number", "null"] },
        "confidenceScore": { "type": "integer" },
        "evidenceQuality": { "type": "string", "enum": ["low", "medium", "high"] },
        "nextAction": { "type": "string" },
        "unknowns": { "type": "array", "items": { "type": "string" } }
      },
      "required": ["businessSummary", "opportunities", "recommendedServices", "suggestedApproach",
                   "estimatedProjectValueMin", "estimatedProjectValueMax", "confidenceScore",
                   "evidenceQuality", "nextAction", "unknowns"]
    }
    """;

    public const string PitchSchema = """
    {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "subject": { "type": ["string", "null"] },
        "body": { "type": "string" },
        "opportunitiesReferenced": { "type": "array", "items": { "type": "string" } }
      },
      "required": ["subject", "body", "opportunitiesReferenced"]
    }
    """;
}

/// <summary>Validates AI output beyond the JSON schema: ranges, required evidence, service whitelist.</summary>
public static class AIResultValidator
{
    private static readonly string[] Confidences = ["low", "medium", "high"];

    public static (LeadAnalysisResult? Result, List<string> Errors) ParseLeadAnalysis(string json, IReadOnlyCollection<string> allowedServices)
    {
        var errors = new List<string>();
        LeadAnalysisResult? r;
        try
        {
            r = JsonSerializer.Deserialize<LeadAnalysisResult>(json, AIJson.Options);
        }
        catch (JsonException ex)
        {
            return (null, [$"Invalid JSON: {ex.Message}"]);
        }
        if (r is null) return (null, ["Empty response."]);

        if (string.IsNullOrWhiteSpace(r.BusinessSummary)) errors.Add("businessSummary is required.");
        if (r.ConfidenceScore is < 0 or > 100) errors.Add("confidenceScore must be between 0 and 100.");
        if (!Confidences.Contains(r.EvidenceQuality?.ToLowerInvariant())) errors.Add("evidenceQuality must be low|medium|high.");
        if (r.EstimatedProjectValueMin is < 0 || r.EstimatedProjectValueMax is < 0) errors.Add("Estimated values cannot be negative.");
        if (r.EstimatedProjectValueMin is not null && r.EstimatedProjectValueMax is not null && r.EstimatedProjectValueMin > r.EstimatedProjectValueMax)
            errors.Add("estimatedProjectValueMin cannot exceed estimatedProjectValueMax.");

        for (var i = 0; i < r.Opportunities.Count; i++)
        {
            var o = r.Opportunities[i];
            if (string.IsNullOrWhiteSpace(o.Title)) errors.Add($"opportunities[{i}].title is required.");
            if (string.IsNullOrWhiteSpace(o.Evidence)) errors.Add($"opportunities[{i}].evidence is required - opportunities without evidence are rejected.");
            if (!Confidences.Contains(o.Confidence?.ToLowerInvariant())) errors.Add($"opportunities[{i}].confidence must be low|medium|high.");
        }

        // Keep only services the owner actually offers (case-insensitive contains match).
        if (allowedServices.Count > 0)
        {
            r.RecommendedServices = r.RecommendedServices
                .Where(s => allowedServices.Any(a => a.Contains(s, StringComparison.OrdinalIgnoreCase) || s.Contains(a, StringComparison.OrdinalIgnoreCase) || SharesKeyword(a, s)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        return (errors.Count == 0 ? r : null, errors);
    }

    public static (PitchResult? Result, List<string> Errors) ParsePitch(string json, string businessName, bool requiresSubject)
    {
        var errors = new List<string>();
        PitchResult? r;
        try
        {
            r = JsonSerializer.Deserialize<PitchResult>(json, AIJson.Options);
        }
        catch (JsonException ex)
        {
            return (null, [$"Invalid JSON: {ex.Message}"]);
        }
        if (r is null) return (null, ["Empty response."]);
        if (string.IsNullOrWhiteSpace(r.Body)) errors.Add("body is required.");
        if (requiresSubject && string.IsNullOrWhiteSpace(r.Subject)) errors.Add("subject is required for email pitches.");
        if (r.Subject is { Length: > 150 }) errors.Add("subject is too long.");
        var firstToken = businessName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? businessName;
        if (!string.IsNullOrWhiteSpace(r.Body) && !r.Body.Contains(firstToken, StringComparison.OrdinalIgnoreCase)
            && !(r.Subject ?? "").Contains(firstToken, StringComparison.OrdinalIgnoreCase))
            errors.Add("Pitch must mention the business name.");
        return (errors.Count == 0 ? r : null, errors);
    }

    private static bool SharesKeyword(string a, string b)
    {
        static IEnumerable<string> Words(string s) => s.ToLowerInvariant()
            .Split([' ', ',', '-', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3);
        var stop = new HashSet<string> { "development", "business", "solutions", "system", "systems", "custom", "application" };
        return Words(a).Where(w => !stop.Contains(w)).Intersect(Words(b).Where(w => !stop.Contains(w))).Any();
    }
}
