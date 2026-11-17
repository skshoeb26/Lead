using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using LeadPilot.Application.Intelligence;
using LeadPilot.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadPilot.Infrastructure.Providers;

/// <summary>
/// Claude via the official Anthropic C# SDK. Uses structured outputs (JSON schema) so responses are machine-validated,
/// records token usage and cost for every call, and enables server-side refusal fallbacks.
/// </summary>
public sealed class ClaudeAIProvider(IOptions<AIOptions> options, IUsageTracker usage, ILogger<ClaudeAIProvider> logger) : IAIProvider
{
    private readonly ClaudeOptions _o = options.Value.Claude;
    private AnthropicClient? _client;

    public string Name => "claude";
    public string Model => _o.Model;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_o.ApiKey);
    public bool IsMock => false;

    private AnthropicClient Client => _client ??= new AnthropicClient
    {
        ApiKey = _o.ApiKey!,
        Timeout = TimeSpan.FromSeconds(_o.TimeoutSeconds),
        MaxRetries = 2
    };

    public async Task<AIJsonResponse> CompleteJsonAsync(AIJsonRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new IntegrationNotConfiguredException("Claude", "Set AI__Claude__ApiKey.");
        var sw = Stopwatch.StartNew();
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.JsonSchema)!;
        var parameters = new MessageCreateParams
        {
            Model = _o.Model,
            MaxTokens = request.MaxTokens ?? _o.MaxTokens,
            System = request.SystemPrompt,
            Messages = [new BetaUserMessageParam { Content = request.UserPrompt }],
            OutputConfig = new BetaOutputConfig
            {
                Format = new BetaJsonOutputFormat { Schema = schema },
                Effort = _o.Effort.ToLowerInvariant() switch { "low" => Effort.Low, "high" => Effort.High, _ => Effort.Medium }
            },
            Betas = _o.EnableFallbacks ? [Anthropic.Models.Beta.AnthropicBeta.ServerSideFallback2026_07_01] : null,
            Fallbacks = _o.EnableFallbacks ? new Default() : null,
        };

        try
        {
            var message = await Client.Beta.Messages.Create(parameters, ct);
            var model = message.Model.ToString() ?? _o.Model;
            var input = message.Usage.InputTokens + (message.Usage.CacheReadInputTokens ?? 0) + (message.Usage.CacheCreationInputTokens ?? 0);
            var output = message.Usage.OutputTokens;
            var cost = EstimateCost(model, input, output);

            if (message.StopReason?.ToString() is "refusal")
                throw new InvalidOperationException("The AI provider declined this request.");
            if (message.StopReason?.ToString() is "max_tokens")
                throw new InvalidOperationException("The AI response was cut off (max tokens). Increase AI__Claude__MaxTokens.");

            var text = string.Concat(message.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
            await usage.RecordAsync(new UsageLog
            {
                Provider = Name, Operation = request.Purpose, Model = model, PromptVersion = request.PromptVersion, LeadId = request.LeadId,
                InputTokens = input, OutputTokens = output, CostUsd = cost, DurationMs = (int)sw.ElapsedMilliseconds
            }, ct);
            return new AIJsonResponse(text, Name, model, input, output, cost, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not IntegrationNotConfiguredException)
        {
            logger.LogWarning(ex, "Claude request failed for {Purpose}", request.Purpose);
            await usage.RecordAsync(new UsageLog
            {
                Provider = Name, Operation = request.Purpose, Model = _o.Model, PromptVersion = request.PromptVersion, LeadId = request.LeadId,
                Success = false, Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message, DurationMs = (int)sw.ElapsedMilliseconds
            }, CancellationToken.None);
            throw;
        }
    }

    private decimal EstimateCost(string model, long input, long output)
    {
        var price = _o.Pricing.FirstOrDefault(p => model.StartsWith(p.Key, StringComparison.OrdinalIgnoreCase)).Value
                    ?? _o.Pricing.GetValueOrDefault(_o.Model) ?? new ModelPrice(0, 0);
        return Math.Round(input / 1_000_000m * price.InputPerMTok + output / 1_000_000m * price.OutputPerMTok, 6);
    }
}

/// <summary>
/// Development/test-only AI provider. Produces deterministic, clearly-labelled ("[MOCK]") output derived solely from
/// the supplied evidence. Never registered in Production.
/// </summary>
public sealed class MockAIProvider(IUsageTracker usage) : IAIProvider
{
    public string Name => "mock";
    public string Model => "mock-model";
    public bool IsConfigured => true;
    public bool IsMock => true;

    public async Task<AIJsonResponse> CompleteJsonAsync(AIJsonRequest request, CancellationToken ct)
    {
        string json;
        if (request.Purpose == "lead-analysis")
        {
            var lines = request.UserPrompt.Split('\n');
            var name = lines.FirstOrDefault(l => l.StartsWith("Name: "))?[6..].Trim() ?? "This business";
            var opps = lines.Where(l => l.StartsWith("- ") && l.Contains("| evidence:")).Take(3).Select(l =>
            {
                var parts = l[2..].Split(" | ");
                return new AIOpportunity
                {
                    Title = parts[0], Problem = "[MOCK] Derived from rule-based evidence.", Evidence = parts.ElementAtOrDefault(1)?.Replace("evidence: ", "") ?? "See website analysis",
                    Assumptions = null, RecommendedService = "Business websites and website redesign", Confidence = "medium"
                };
            }).ToList();
            json = JsonSerializer.Serialize(new LeadAnalysisResult
            {
                BusinessSummary = $"[MOCK] {name} — summary generated by the development mock provider from supplied evidence only.",
                Opportunities = opps,
                RecommendedServices = ["Business websites and website redesign"],
                SuggestedApproach = "[MOCK] Reference the observed website gaps and offer a short call.",
                EstimatedProjectValueMin = opps.Count > 0 ? 15000 : null,
                EstimatedProjectValueMax = opps.Count > 0 ? 35000 : null,
                ConfidenceScore = opps.Count > 0 ? 55 : 20,
                EvidenceQuality = opps.Count > 0 ? "medium" : "low",
                NextAction = opps.Count > 0 ? "Generate personalised email" : "Review manually",
                Unknowns = ["Budget", "Existing software", "Decision maker"]
            }, AIJson.Options);
        }
        else
        {
            var name = request.UserPrompt.Split('\n').FirstOrDefault(l => l.StartsWith("Business name: "))?[15..].Trim() ?? "your business";
            var opp = request.UserPrompt.Split('\n').FirstOrDefault(l => l.StartsWith("- "))?[2..].Split(':')[0] ?? "your website";
            var subject = request.Purpose.Contains("WhatsApp") || request.Purpose.Contains("LinkedIn") || request.Purpose.Contains("ShortIntro") ? null : $"[MOCK] A quick idea for {name}";
            json = JsonSerializer.Serialize(new PitchResult
            {
                Subject = subject,
                Body = $"Hi,\n\n[MOCK DRAFT] I was looking at {name}'s website and noticed: {opp.ToLowerInvariant()}. I build simple web solutions that help with this.\n\nWould you be open to a short call this week?\n\nRegards",
                OpportunitiesReferenced = [opp]
            }, AIJson.Options);
        }
        await usage.RecordAsync(new UsageLog { Provider = Name, Operation = request.Purpose, Model = Model, PromptVersion = request.PromptVersion, LeadId = request.LeadId }, ct);
        return new AIJsonResponse(json, Name, Model, 0, 0, 0, 1);
    }
}

/// <summary>Registered when no AI credentials exist in Production: every call reports "not configured".</summary>
public sealed class UnconfiguredAIProvider : IAIProvider
{
    public string Name => "claude";
    public string Model => "n/a";
    public bool IsConfigured => false;
    public bool IsMock => false;
    public Task<AIJsonResponse> CompleteJsonAsync(AIJsonRequest request, CancellationToken ct) =>
        throw new IntegrationNotConfiguredException("Claude", "Set AI__Claude__ApiKey on the server.");
}
