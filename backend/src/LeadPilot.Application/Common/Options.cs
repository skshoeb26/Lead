namespace LeadPilot.Application.Common;

// ---------------------------------------------------------------------------
// Static configuration bound from appsettings / environment variables.
// Secrets (keys, client secrets) only ever come from environment variables or
// a secret store - never from source control.
// ---------------------------------------------------------------------------

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "leadpilot";
    public string Audience { get; set; } = "leadpilot-app";
    /// <summary>At least 32 random bytes. Env: Jwt__SigningKey.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}

public sealed class AuthOptions
{
    public const string Section = "Auth";
    /// <summary>When false, registration is only possible while no user exists (owner bootstrap).</summary>
    public bool AllowRegistration { get; set; }
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public string? BootstrapOwnerEmail { get; set; }
    public string? BootstrapOwnerPassword { get; set; }
}

public sealed class ProvidersOptions
{
    public const string Section = "Providers";
    /// <summary>
    /// Use mock providers when real credentials are missing. Forced to false in Production
    /// so fake data is never silently substituted.
    /// </summary>
    public bool UseMocksWhenUnconfigured { get; set; }
}

public sealed class GooglePlacesOptions
{
    public const string Section = "GooglePlaces";
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://places.googleapis.com/v1/";
    public string RegionCode { get; set; } = "IN";
    public string LanguageCode { get; set; } = "en";
    /// <summary>Estimated cost per Text Search request (USD) for the requested field mask. Verify against current Google pricing.</summary>
    public decimal CostPerRequestUsd { get; set; } = 0.035m;
    public int PageSize { get; set; } = 20;
    public int RequestsPerMinute { get; set; } = 60;
    /// <summary>Provider content (other than place id) older than this is refreshed or purged by the retention job.</summary>
    public int ContentRetentionDays { get; set; } = 30;
    public bool RefreshContentOnExpiry { get; set; }
    public string Attribution { get; set; } = "Business data © Google Maps";
}

public sealed class PageSpeedOptions
{
    public const string Section = "PageSpeed";
    public string? ApiKey { get; set; }
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://www.googleapis.com/pagespeedonline/v5/runPagespeed";
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class WebsiteAnalysisOptions
{
    public const string Section = "WebsiteAnalysis";
    public int TimeoutSeconds { get; set; } = 15;
    public long MaxResponseBytes { get; set; } = 2 * 1024 * 1024;
    public int MaxRedirects { get; set; } = 5;
    public int MaxExtraPages { get; set; } = 4;
    public int MaxLinksToCheck { get; set; } = 10;
    public int[] AllowedPorts { get; set; } = [80, 443];
    public string UserAgent { get; set; } = "LeadPilotBot/1.0 (+website-quality-check)";
    public bool RespectRobotsTxt { get; set; } = true;
}

public sealed class AIOptions
{
    public const string Section = "AI";
    /// <summary>Active provider key: "claude" or "mock".</summary>
    public string Provider { get; set; } = "claude";
    public ClaudeOptions Claude { get; set; } = new();
}

public sealed class ClaudeOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-opus-5-5";
    /// <summary>low | medium | high. Lower effort = fewer tokens = lower cost.</summary>
    public string Effort { get; set; } = "medium";
    public int MaxTokens { get; set; } = 8000;
    public int TimeoutSeconds { get; set; } = 120;
    /// <summary>Enable server-side refusal fallbacks (beta).</summary>
    public bool EnableFallbacks { get; set; } = true;
    /// <summary>USD per million tokens, keyed by model id. Used for cost tracking.</summary>
    public Dictionary<string, ModelPrice> Pricing { get; set; } = new()
    {
        ["claude-opus-5-5"] = new(4.00m, 20.00m),
        ["claude-sonnet-5-5"] = new(2.00m, 10.00m),
        ["claude-haiku-5-5"] = new(0.10m, 0.50m),
    };
}

public sealed record ModelPrice(decimal InputPerMTok, decimal OutputPerMTok)
{
    public ModelPrice() : this(0, 0) { }
}

public sealed class GoogleOAuthOptions
{
    public const string Section = "GoogleOAuth";
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    /// <summary>Must exactly match an authorised redirect URI in Google Cloud console, e.g. https://api.example.com/api/v1/integrations/google/callback</summary>
    public string? RedirectUri { get; set; }
    /// <summary>Where the browser is sent after the callback. Defaults to the app deep link.</summary>
    public string AppReturnUrl { get; set; } = "leadpilot://settings/integrations";
    public string[] Scopes { get; set; } = ["openid", "email", "https://www.googleapis.com/auth/gmail.send"];
    public int StateLifetimeMinutes { get; set; } = 10;
}

public sealed class EmailOptions
{
    public const string Section = "Email";
    /// <summary>
    /// Test mode: Gmail is never called. Sends are recorded as simulated. Defaults to TRUE so a
    /// fresh install can never email real prospects by accident. Set Email__TestMode=false in production.
    /// </summary>
    public bool TestMode { get; set; } = true;
    public string GmailBaseUrl { get; set; } = "https://gmail.googleapis.com/gmail/v1/";
    public string TokenEndpoint { get; set; } = "https://oauth2.googleapis.com/token";
    public string RevokeEndpoint { get; set; } = "https://oauth2.googleapis.com/revoke";
}

public sealed class FirebaseOptions
{
    public const string Section = "Firebase";
    public string? ProjectId { get; set; }
    /// <summary>Path to the service-account JSON file (mounted secret), or the JSON itself via ServiceAccountJson.</summary>
    public string? ServiceAccountFile { get; set; }
    public string? ServiceAccountJson { get; set; }
}

public sealed class JobsOptions
{
    public const string Section = "Jobs";
    /// <summary>Use Hangfire. Disable only for tests.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Run the Hangfire server inside the API process (single-container deployments). Otherwise run LeadPilot.Worker.</summary>
    public bool RunServerInApi { get; set; }
    public int WorkerCount { get; set; } = 4;
    public string OutboxCron { get; set; } = "* * * * *";
    public string FollowUpCron { get; set; } = "*/15 * * * *";
    public string MaintenanceCron { get; set; } = "30 2 * * *";
    public string BudgetCheckCron { get; set; } = "0 * * * *";
}

public sealed class CorsOptions
{
    public const string Section = "Cors";
    public string[] AllowedOrigins { get; set; } = ["capacitor://localhost", "https://localhost", "http://localhost", "http://localhost:4200"];
}
