using System.Net;
using LeadPilot.Application.Common;
using LeadPilot.Application.Compliance;
using LeadPilot.Application.Outreach;
using LeadPilot.Domain;
using LeadPilot.Infrastructure.Gmail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LeadPilot.API.Controllers;

[ApiController]
[Route("api/v1/integrations/google")]
public sealed class GoogleIntegrationController(GoogleOAuthService oauth, ICurrentUser user, IOptions<GoogleOAuthOptions> options, ILogger<GoogleIntegrationController> logger) : ControllerBase
{
    /// <summary>Returns the Google consent URL (the app opens it in the system browser).</summary>
    [HttpGet("connect")]
    public async Task<ActionResult<object>> Connect(CancellationToken ct) =>
        Ok(new { authorizationUrl = await oauth.CreateAuthorizationUrlAsync(user.RequireUserId(), ct) });

    /// <summary>OAuth redirect target. Anonymous by design: the single-use state parameter binds the response to the user who started the flow.</summary>
    [HttpGet("callback"), AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        string title, message;
        var ok = false;
        try
        {
            var email = await oauth.HandleCallbackAsync(code, state, error, ct);
            title = "Gmail connected";
            message = $"LeadPilot AI can now send emails you approve from {WebUtility.HtmlEncode(email)}.";
            ok = true;
        }
        catch (AppException ex)
        {
            logger.LogWarning("Gmail OAuth callback failed: {Message}", ex.Message);
            title = "Gmail connection failed";
            message = WebUtility.HtmlEncode(ex.Message);
        }
        var returnUrl = WebUtility.HtmlEncode(options.Value.AppReturnUrl + (ok ? "?gmail=connected" : "?gmail=failed"));
        var html = $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{title}}</title><style>body{font-family:system-ui,sans-serif;max-width:480px;margin:15vh auto;padding:0 16px;text-align:center;color:#1f2937}
            a{display:inline-block;margin-top:24px;padding:12px 20px;background:#4f46e5;color:#fff;border-radius:10px;text-decoration:none}</style></head>
            <body><h1>{{title}}</h1><p>{{message}}</p><a href="{{returnUrl}}">Return to LeadPilot AI</a></body></html>
            """;
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'";
        return Content(html, "text/html");
    }

    [HttpGet("status")]
    public async Task<ActionResult<GoogleConnectionStatus>> Status(CancellationToken ct) => Ok(await oauth.StatusAsync(user.RequireUserId(), ct));

    [HttpDelete("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await oauth.DisconnectAsync(user.RequireUserId(), ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/emails")]
[Produces("application/json")]
public sealed class EmailsController(EmailService emails) : ControllerBase
{
    [HttpGet("pending")]
    public async Task<ActionResult<PagedResult<EmailDraftDto>>> Pending([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await emails.ListAsync([EmailStatus.PendingApproval, EmailStatus.Approved, EmailStatus.Expired, EmailStatus.Failed, EmailStatus.OutcomeUnknown], null, page, pageSize, ct));

    [HttpGet]
    public async Task<ActionResult<PagedResult<EmailDraftDto>>> List([FromQuery] EmailStatus[]? status, [FromQuery] Guid? leadId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await emails.ListAsync(status, leadId, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmailDraftDto>> Get(Guid id, CancellationToken ct) => Ok(await emails.GetAsync(id, ct));

    /// <summary>Creates a draft (from a pitch or manual) directly in Pending Approval.</summary>
    [HttpPost]
    public async Task<ActionResult<EmailDraftDto>> Create(CreateEmailDraftRequest request, CancellationToken ct) => Ok(await emails.CreateDraftAsync(request, ct));

    /// <summary>Edits recipient/subject/body. Any change invalidates an existing approval.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EmailDraftDto>> Update(Guid id, UpdateEmailDraftRequest request, CancellationToken ct) => Ok(await emails.UpdateDraftAsync(id, request, ct));

    /// <summary>Approves the exact content version the user reviewed (hash + version must match).</summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<EmailDraftDto>> Approve(Guid id, ApproveEmailRequest request, CancellationToken ct) => Ok(await emails.ApproveAsync(id, request, ct));

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<EmailDraftDto>> Reject(Guid id, RejectEmailRequest request, CancellationToken ct) => Ok(await emails.RejectAsync(id, request, ct));

    /// <summary>Dry-run of every server-side send check.</summary>
    [HttpGet("{id:guid}/send-check")]
    public async Task<ActionResult<SendCheckDto>> SendCheck(Guid id, CancellationToken ct) => Ok(await emails.CheckSendAsync(id, ct));

    /// <summary>Queues an approved email in the durable outbox after full server-side validation.</summary>
    [HttpPost("{id:guid}/send")]
    public async Task<ActionResult<EmailDraftDto>> Send(Guid id, CancellationToken ct) => Accepted(await emails.SendAsync(id, ct));

    [HttpPost("{id:guid}/resolve-unknown")]
    public async Task<ActionResult<EmailDraftDto>> ResolveUnknown(Guid id, ResolveUnknownRequest request, CancellationToken ct) => Ok(await emails.ResolveUnknownAsync(id, request, ct));

    [HttpGet("{id:guid}/audit")]
    public async Task<ActionResult<List<EmailAuditDto>>> Audit(Guid id, CancellationToken ct) => Ok(await emails.AuditAsync(id, ct));
}

[ApiController]
[Route("api/v1/compliance")]
[Produces("application/json")]
public sealed class ComplianceController(ConsentService consent) : ControllerBase
{
    [HttpGet("suppressions")]
    public async Task<ActionResult<List<SuppressionDto>>> Suppressions(CancellationToken ct) => Ok(await consent.ListSuppressionsAsync(ct));

    [HttpPost("suppressions")]
    public async Task<ActionResult<SuppressionDto>> AddSuppression(AddSuppressionRequest request, CancellationToken ct) => Ok(await consent.AddSuppressionAsync(request, ct));

    [HttpDelete("suppressions/{id:guid}")]
    public async Task<IActionResult> RemoveSuppression(Guid id, CancellationToken ct)
    {
        await consent.RemoveSuppressionAsync(id, ct);
        return NoContent();
    }

    [HttpGet("consents")]
    public async Task<ActionResult<List<ConsentDto>>> Consents([FromQuery] Guid? leadId, CancellationToken ct) => Ok(await consent.ListConsentsAsync(leadId, ct));

    [HttpPost("consents")]
    public async Task<ActionResult<ConsentDto>> RecordConsent(RecordConsentRequest request, CancellationToken ct) => Ok(await consent.RecordConsentAsync(request, ct));

    [HttpPost("consents/{id:guid}/revoke")]
    public async Task<IActionResult> RevokeConsent(Guid id, CancellationToken ct)
    {
        await consent.RevokeConsentAsync(id, ct);
        return NoContent();
    }
}
