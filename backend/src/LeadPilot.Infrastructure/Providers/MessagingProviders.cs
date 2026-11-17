using System.Net.Http.Json;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadPilot.Infrastructure.Providers;

/// <summary>Firebase Cloud Messaging HTTP v1 using a service-account credential (server-side only).</summary>
public sealed class FcmNotificationProvider(IHttpClientFactory factory, IOptions<FirebaseOptions> options, ILogger<FcmNotificationProvider> logger) : INotificationProvider
{
    public const string HttpClientName = "fcm";
    private GoogleCredential? _credential;
    private string? _projectId;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ServiceAccountJson) ||
                                (!string.IsNullOrWhiteSpace(options.Value.ServiceAccountFile) && File.Exists(options.Value.ServiceAccountFile));

    private async Task<(string Token, string Project)> GetTokenAsync(CancellationToken ct)
    {
        if (_credential is null)
        {
            var json = options.Value.ServiceAccountJson ?? await File.ReadAllTextAsync(options.Value.ServiceAccountFile!, ct);
            using var doc = JsonDocument.Parse(json);
            _projectId = options.Value.ProjectId ?? doc.RootElement.GetProperty("project_id").GetString();
#pragma warning disable CS0618 // FromJson is acceptable for a trusted, operator-supplied service-account file
            _credential = GoogleCredential.FromJson(json).CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
#pragma warning restore CS0618
        }
        var token = await ((ITokenAccess)_credential).GetAccessTokenForRequestAsync(cancellationToken: ct);
        return (token, _projectId!);
    }

    public async Task<IReadOnlyList<string>> SendAsync(IReadOnlyList<string> deviceTokens, PushMessage message, CancellationToken ct)
    {
        if (!IsConfigured) return [];
        var (accessToken, project) = await GetTokenAsync(ct);
        var client = factory.CreateClient(HttpClientName);
        var invalid = new List<string>();
        foreach (var token in deviceTokens)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{project}/messages:send")
            {
                Content = JsonContent.Create(new
                {
                    message = new
                    {
                        token,
                        notification = new { title = message.Title, body = message.Body },
                        data = message.Data,
                        android = new { priority = "high", notification = new { channel_id = "leadpilot_default", click_action = "FCM_PLUGIN_ACTIVITY" } }
                    }
                })
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) continue;
            var body = await response.Content.ReadAsStringAsync(ct);
            if ((int)response.StatusCode is 404 or 400 && (body.Contains("UNREGISTERED") || body.Contains("INVALID_ARGUMENT"))) invalid.Add(token);
            else logger.LogWarning("FCM send failed {Status}: {Body}", (int)response.StatusCode, body.Length > 300 ? body[..300] : body);
        }
        return invalid;
    }
}

/// <summary>
/// Official WhatsApp Business Platform (Cloud API) template sender. Only used after the business account is configured
/// AND a recorded opt-in exists (enforced by ConsentService). The initial release generates drafts only.
/// </summary>
public sealed class WhatsAppCloudProvider(IHttpClientFactory factory, IConfiguration config) : IWhatsAppProvider
{
    private string? Token => config["WhatsApp:AccessToken"];
    private string? PhoneNumberId => config["WhatsApp:PhoneNumberId"];
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(PhoneNumberId) && config.GetValue<bool>("WhatsApp:Enabled");

    public async Task<WhatsAppSendResult> SendTemplateAsync(string toPhoneE164, string templateName, string languageCode, IReadOnlyList<string> parameters, CancellationToken ct)
    {
        if (!IsConfigured) throw new IntegrationNotConfiguredException("WhatsApp Cloud API", "WhatsApp sending is disabled. Configure WhatsApp__Enabled, WhatsApp__AccessToken and WhatsApp__PhoneNumberId after setting up opt-in collection.");
        var client = factory.CreateClient("whatsapp");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/v21.0/{PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp", to = toPhoneE164.TrimStart('+'), type = "template",
                template = new { name = templateName, language = new { code = languageCode }, components = new[] { new { type = "body", parameters = parameters.Select(p => new { type = "text", text = p }) } } }
            })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return new WhatsAppSendResult(false, null, body.Length > 300 ? body[..300] : body);
        using var doc = JsonDocument.Parse(body);
        return new WhatsAppSendResult(true, doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString(), null);
    }
}

public sealed class NullNotificationProvider : INotificationProvider
{
    public bool IsConfigured => false;
    public Task<IReadOnlyList<string>> SendAsync(IReadOnlyList<string> deviceTokens, PushMessage message, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
}
