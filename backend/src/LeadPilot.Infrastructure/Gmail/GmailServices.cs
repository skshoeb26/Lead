using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;
using LeadPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LeadPilot.Infrastructure.Gmail;

public sealed record GoogleConnectionStatus(bool Configured, bool Connected, string? EmailAddress, string? Scopes, EmailAccountStatus? Status, DateTime? ConnectedAt, string? LastError, bool TestMode);

/// <summary>
/// Google OAuth 2.0 authorization-code flow with PKCE (S256) and a single-use, hashed, expiring state parameter.
/// Requests only gmail.send (+ openid/email to display the connected address). Refresh tokens are encrypted at rest
/// and never leave the server.
/// </summary>
public sealed class GoogleOAuthService(
    AppDbContext db,
    ISecretProtector protector,
    IHttpClientFactory httpFactory,
    IOptions<GoogleOAuthOptions> options,
    IOptions<EmailOptions> emailOptions,
    IClock clock,
    IAuditService audit,
    ILogger<GoogleOAuthService> logger)
{
    public const string HttpClientName = "google-oauth";
    private const string RefreshPurpose = "gmail-refresh-token";
    private const string VerifierPurpose = "oauth-pkce-verifier";
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

    private GoogleOAuthOptions O => options.Value;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(O.ClientId) && !string.IsNullOrWhiteSpace(O.ClientSecret) && !string.IsNullOrWhiteSpace(O.RedirectUri);

    public async Task<string> CreateAuthorizationUrlAsync(Guid userId, CancellationToken ct)
    {
        if (!IsConfigured) throw new IntegrationNotConfiguredException("Google OAuth", "Set GoogleOAuth__ClientId, GoogleOAuth__ClientSecret and GoogleOAuth__RedirectUri (see docs/SETUP-GMAIL.md).");
        if (!Uri.TryCreate(O.RedirectUri, UriKind.Absolute, out var redirect) || (redirect.Scheme != "https" && redirect.Host != "localhost"))
            throw new ValidationFailedException("GoogleOAuth:RedirectUri must be an absolute HTTPS URL.");

        var state = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        db.OAuthStates.Add(new OAuthState
        {
            UserId = userId, StateHash = Hash(state), EncryptedCodeVerifier = protector.Protect(VerifierPurpose, verifier),
            RedirectUri = O.RedirectUri!, ExpiresAt = clock.UtcNow.AddMinutes(O.StateLifetimeMinutes)
        });
        await db.SaveChangesAsync(ct);

        var query = new Dictionary<string, string>
        {
            ["client_id"] = O.ClientId!, ["redirect_uri"] = O.RedirectUri!, ["response_type"] = "code",
            ["scope"] = string.Join(' ', O.Scopes), ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256",
            ["access_type"] = "offline", ["prompt"] = "consent", ["include_granted_scopes"] = "false"
        };
        return AuthEndpoint + "?" + string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    }

    /// <summary>Validates state (exists, unexpired, unused), exchanges the code with the PKCE verifier and stores the encrypted refresh token.</summary>
    public async Task<string> HandleCallbackAsync(string? code, string? state, string? error, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error)) throw new ValidationFailedException($"Google returned an error: {error}");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)) throw new ValidationFailedException("Missing code or state.");
        var stateRow = await ValidateAndConsumeStateAsync(state, ct);

        var verifier = protector.Unprotect(VerifierPurpose, stateRow.EncryptedCodeVerifier);
        var client = httpFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsync(emailOptions.Value.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code, ["client_id"] = O.ClientId!, ["client_secret"] = O.ClientSecret!, ["redirect_uri"] = stateRow.RedirectUri,
            ["grant_type"] = "authorization_code", ["code_verifier"] = verifier
        }), ct);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (!response.IsSuccessStatusCode || token?.AccessToken is null)
        {
            logger.LogWarning("Google token exchange failed with {Status}: {Error}", (int)response.StatusCode, token?.Error);
            throw new ValidationFailedException($"Google token exchange failed: {token?.ErrorDescription ?? token?.Error ?? response.StatusCode.ToString()}");
        }
        if (token.Scope is null || !token.Scope.Split(' ').Contains("https://www.googleapis.com/auth/gmail.send"))
            throw new ValidationFailedException("The Gmail send permission was not granted. Please allow 'Send email on your behalf'.");
        if (string.IsNullOrEmpty(token.RefreshToken))
            throw new ValidationFailedException("Google did not return a refresh token. Remove LeadPilot from your Google account's third-party access and connect again.");

        var email = ReadEmailFromIdToken(token.IdToken) ?? "unknown";
        var account = await db.EmailAccounts.FirstOrDefaultAsync(a => a.OwnerUserId == stateRow.UserId && a.Provider == "google" && a.EmailAddress == email, ct);
        if (account is null)
        {
            account = new EmailAccount { OwnerUserId = stateRow.UserId, Provider = "google", EmailAddress = email };
            db.EmailAccounts.Add(account);
        }
        // Single connected Gmail account per user: disconnect others.
        foreach (var other in await db.EmailAccounts.Where(a => a.OwnerUserId == stateRow.UserId && a.Id != account.Id && a.Status == EmailAccountStatus.Connected).ToListAsync(ct))
            other.Status = EmailAccountStatus.Disconnected;
        account.EncryptedRefreshToken = protector.Protect(RefreshPurpose, token.RefreshToken);
        account.Scopes = token.Scope;
        account.Status = EmailAccountStatus.Connected;
        account.ConnectedAt = clock.UtcNow;
        account.LastError = null;
        await db.SaveChangesAsync(ct);
        AccessTokenCache.Set(account.Id, token.AccessToken, clock.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 60)));
        await audit.LogAsync("gmail.connected", nameof(EmailAccount), account.Id.ToString(), $"scopes={token.Scope}", ct);
        return email;
    }

    public async Task<OAuthState> ValidateAndConsumeStateAsync(string state, CancellationToken ct)
    {
        var hash = Hash(state);
        var row = await db.OAuthStates.FirstOrDefaultAsync(s => s.StateHash == hash, ct) ?? throw new ValidationFailedException("Invalid OAuth state.");
        if (row.ConsumedAt is not null) throw new ValidationFailedException("OAuth state was already used.");
        if (row.ExpiresAt <= clock.UtcNow) throw new ValidationFailedException("OAuth state expired. Start the connection again.");
        if (!string.Equals(row.RedirectUri, O.RedirectUri, StringComparison.Ordinal)) throw new ValidationFailedException("Redirect URI mismatch.");
        row.ConsumedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<GoogleConnectionStatus> StatusAsync(Guid userId, CancellationToken ct)
    {
        var account = await db.EmailAccounts.AsNoTracking().Where(a => a.OwnerUserId == userId && a.Provider == "google")
            .OrderByDescending(a => a.Status == EmailAccountStatus.Connected).ThenByDescending(a => a.ConnectedAt).FirstOrDefaultAsync(ct);
        return new GoogleConnectionStatus(IsConfigured, account?.Status == EmailAccountStatus.Connected, account?.EmailAddress, account?.Scopes,
            account?.Status, account?.ConnectedAt, account?.LastError, emailOptions.Value.TestMode);
    }

    public async Task DisconnectAsync(Guid userId, CancellationToken ct)
    {
        var accounts = await db.EmailAccounts.Where(a => a.OwnerUserId == userId && a.Provider == "google" && a.Status != EmailAccountStatus.Disconnected).ToListAsync(ct);
        foreach (var a in accounts)
        {
            try
            {
                var refresh = protector.Unprotect(RefreshPurpose, a.EncryptedRefreshToken);
                var client = httpFactory.CreateClient(HttpClientName);
                await client.PostAsync(emailOptions.Value.RevokeEndpoint, new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh }), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Token revocation at Google failed for account {AccountId}; removing locally anyway", a.Id);
            }
            a.Status = EmailAccountStatus.Disconnected;
            a.EncryptedRefreshToken = string.Empty;
            AccessTokenCache.Remove(a.Id);
        }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("gmail.disconnected", nameof(EmailAccount), userId.ToString(), null, ct);
    }

    /// <summary>Returns a valid access token, refreshing with the stored refresh token when needed.</summary>
    public async Task<string> GetAccessTokenAsync(EmailAccount account, CancellationToken ct)
    {
        if (AccessTokenCache.TryGet(account.Id, clock.UtcNow, out var cached)) return cached;
        if (string.IsNullOrEmpty(account.EncryptedRefreshToken)) throw new UnauthorizedException("Gmail is not connected.", "gmail_not_connected");
        var refresh = protector.Unprotect(RefreshPurpose, account.EncryptedRefreshToken);
        var client = httpFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsync(emailOptions.Value.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = O.ClientId!, ["client_secret"] = O.ClientSecret!, ["refresh_token"] = refresh, ["grant_type"] = "refresh_token"
        }), ct);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        var tracked = await db.EmailAccounts.FirstAsync(a => a.Id == account.Id, ct);
        if (!response.IsSuccessStatusCode || token?.AccessToken is null)
        {
            // invalid_grant = revoked or expired refresh token: the user must reconnect.
            tracked.Status = token?.Error == "invalid_grant" ? EmailAccountStatus.Revoked : EmailAccountStatus.Error;
            tracked.LastError = $"Token refresh failed: {token?.Error ?? response.StatusCode.ToString()}";
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException("Gmail authorisation expired or was revoked. Reconnect Gmail in Settings.", "gmail_reconnect_required");
        }
        tracked.LastTokenRefreshAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        AccessTokenCache.Set(account.Id, token.AccessToken, clock.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 60)));
        return token.AccessToken;
    }

    private static string? ReadEmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken)) return null;
        // The id_token comes directly from Google's token endpoint over TLS (back channel), so reading its claims is sufficient here.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(idToken);
        return jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
    }
}

/// <summary>In-memory access-token cache (access tokens are short-lived and never persisted).</summary>
internal static class AccessTokenCache
{
    private static readonly ConcurrentDictionary<Guid, (string Token, DateTime Expires)> Cache = new();
    public static void Set(Guid id, string token, DateTime expires) => Cache[id] = (token, expires);
    public static void Remove(Guid id) => Cache.TryRemove(id, out _);
    public static bool TryGet(Guid id, DateTime now, out string token)
    {
        token = "";
        if (!Cache.TryGetValue(id, out var v) || v.Expires <= now) return false;
        token = v.Token;
        return true;
    }
}

/// <summary>Sends through the Gmail API users.messages.send endpoint.</summary>
public sealed class GmailEmailProvider(GoogleOAuthService oauth, IHttpClientFactory httpFactory, IOptions<EmailOptions> options, IUsageTracker usage, ILogger<GmailEmailProvider> logger) : IEmailProvider
{
    public const string HttpClientName = "gmail";
    public string Name => "gmail";

    public async Task<EmailSendResult> SendAsync(EmailAccount account, OutgoingEmail email, CancellationToken ct)
    {
        string accessToken;
        try { accessToken = await oauth.GetAccessTokenAsync(account, ct); }
        catch (UnauthorizedException ex) { return new EmailSendResult(false, null, null, 401, ex.Message, false, false); }
        catch (HttpRequestException ex) { return new EmailSendResult(false, null, null, null, $"Token refresh failed: {ex.Message}", false, true); }

        var raw = MimeBuilder.ToBase64Url(MimeBuilder.Build(email));
        var client = httpFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.Value.GmailBaseUrl), "users/me/messages/send"))
        {
            Content = JsonContent.Create(new { raw })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError)
        {
            // The connection was never established, so Gmail cannot have received the message: safe to retry.
            return new EmailSendResult(false, null, null, null, $"Could not connect to Gmail: {ex.Message}", false, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Request may have been delivered but the response was lost.
            logger.LogWarning(ex, "Gmail send outcome unknown");
            return new EmailSendResult(false, null, null, null, $"Response lost: {ex.Message}", true, false);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
            await usage.RecordAsync(new UsageLog { Provider = "gmail", Operation = "messages.send", Success = response.IsSuccessStatusCode, Error = response.IsSuccessStatusCode ? null : Trim(body) }, CancellationToken.None);
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    return new EmailSendResult(true, doc.RootElement.GetProperty("id").GetString(),
                        doc.RootElement.TryGetProperty("threadId", out var t) ? t.GetString() : null, (int)response.StatusCode, null, false, false);
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
                {
                    // 2xx means Gmail accepted it even if we cannot parse the id.
                    return new EmailSendResult(true, null, null, (int)response.StatusCode, "Accepted, but response could not be parsed", false, false);
                }
            }
            var status = (int)response.StatusCode;
            var retryable = status is 429 or 500 or 502 or 503 or 504;
            // A 5xx from Gmail's front end could in rare cases follow acceptance; Google documents 5xx as safe to retry with backoff.
            return new EmailSendResult(false, null, null, status, $"Gmail API {status}: {Trim(body)}", false, retryable);
        }
    }

    private static string Trim(string s) => s.Length > 500 ? s[..500] : s;
}

/// <summary>
/// Used whenever Email:TestMode is true. Never contacts Gmail or any mail server; returns a simulated acceptance
/// so the full approval/outbox workflow can be exercised safely.
/// </summary>
public sealed class TestModeEmailProvider(ILogger<TestModeEmailProvider> logger) : IEmailProvider
{
    public string Name => "test-mode";
    public List<OutgoingEmail> Sent { get; } = [];

    public Task<EmailSendResult> SendAsync(EmailAccount account, OutgoingEmail email, CancellationToken ct)
    {
        var mime = MimeBuilder.Build(email); // still validates headers
        lock (Sent) Sent.Add(email);
        logger.LogInformation("[TEST MODE] Email to {Recipient} NOT sent ({Bytes} bytes MIME)", email.To, mime.Length);
        return Task.FromResult(new EmailSendResult(true, $"test-{Guid.NewGuid():N}", null, 200, null, false, false));
    }
}
