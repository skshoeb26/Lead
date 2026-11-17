using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Auth;
using LeadPilot.Application.Common;
using LeadPilot.Application.Compliance;
using LeadPilot.Application.Leads;
using LeadPilot.Application.Outreach;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using LeadPilot.Infrastructure.Gmail;
using LeadPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeadPilot.Tests.Integration;

public class AuthApiTests : IAsyncLifetime
{
    private readonly TestApp _app = new();
    public Task InitializeAsync() => _app.InitAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Protected_endpoints_require_authentication()
    {
        var client = _app.CreateClient();
        (await client.GetAsync("/api/v1/leads")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/emails/pending")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Registration_is_closed_after_owner_exists()
    {
        await _app.OwnerClientAsync();
        var r = await _app.CreateClient().PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("other@test.in", "Str0ngPassword!", "Other"));
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Weak_password_is_rejected()
    {
        var r = await _app.CreateClient().PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("o@test.in", "short", "O"));
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_token_rotates_and_reuse_revokes_family()
    {
        await _app.OwnerClientAsync();
        var client = _app.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner@test.in", "Str0ngPassword!", "test"))).Content.ReadFromJsonAsync<AuthResponse>(TestApp.Json);

        var first = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login!.RefreshToken));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await first.Content.ReadFromJsonAsync<AuthResponse>(TestApp.Json);
        rotated!.RefreshToken.Should().NotBe(login.RefreshToken);

        // Reusing the old token is detected and revokes the whole family, including the new token.
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(rotated.RefreshToken))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_password_fails_and_locks_out_after_limit()
    {
        await _app.OwnerClientAsync();
        var client = _app.CreateClient();
        for (var i = 0; i < 5; i++)
            (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner@test.in", "wrong-password", null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var locked = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner@test.in", "Str0ngPassword!", null));
        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await locked.Content.ReadAsStringAsync()).Should().Contain("locked_out");
    }
}

public class LeadApiTests : IAsyncLifetime
{
    private readonly TestApp _app = new();
    public Task InitializeAsync() => _app.InitAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Create_get_and_reject_exact_duplicate()
    {
        var client = await _app.OwnerClientAsync();
        var req = new CreateLeadRequest { BusinessName = "Sunrise Dental Clinic", WebsiteUrl = "https://sunrisedental.example", Email = "hello@sunrisedental.example", ContactSourceUrl = "https://sunrisedental.example/contact", City = "Pune" };
        var created = await client.PostAsJsonAsync("/api/v1/leads", req);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetFromJsonAsync<PagedResult<LeadListItem>>("/api/v1/leads?search=sunrise", TestApp.Json);
        list!.TotalCount.Should().Be(1);
        var detail = await client.GetFromJsonAsync<LeadDetail>($"/api/v1/leads/{list.Items[0].Id}", TestApp.Json);
        detail!.Contacts.Should().ContainSingle(c => c.Type == ContactType.Email && c.SourceVerified && c.SyntaxVerified && !c.DeliveryVerified && !c.ConsentVerified);

        var dup = await client.PostAsJsonAsync("/api/v1/leads", req with { BusinessName = "Sunrise Dental" });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Contact_without_source_is_rejected()
    {
        var client = await _app.OwnerClientAsync();
        var r = await client.PostAsJsonAsync("/api/v1/leads", new CreateLeadRequest { BusinessName = "No Source Co", Email = "a@nosource.example" });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Csv_import_creates_leads_skips_invalid_and_duplicates()
    {
        var client = await _app.OwnerClientAsync();
        const string csv = "business_name,category,website,phone,email,city,state,pincode\n" +
                           "Alpha Coaching,Coaching institutes,https://alpha.example,+91 98765 11111,info@alpha.example,Mumbai,Maharashtra,400001\n" +
                           "Beta Clinic,Clinics & hospitals,,,bad-email,Pune,Maharashtra,411001\n" +
                           "Alpha Coaching Duplicate,,https://www.alpha.example,,,Mumbai,,\n" +
                           ",,,,,,,\n";
        using var content = new MultipartFormDataContent { { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv)), "file", "leads.csv" } };
        var r = await client.PostAsync("/api/v1/leads/import", content);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await r.Content.ReadFromJsonAsync<CsvImportResult>(TestApp.Json);
        result!.Created.Should().Be(1);
        result.Duplicates.Should().Be(1);
        result.Skipped.Should().Be(2);
    }

    [Fact]
    public async Task Merge_moves_contacts_and_hides_duplicate()
    {
        var client = await _app.OwnerClientAsync();
        var a = await (await client.PostAsJsonAsync("/api/v1/leads", new CreateLeadRequest { BusinessName = "Gamma Gym", City = "Surat" })).Content.ReadFromJsonAsync<LeadPilot.API.Controllers.LeadsController.CreateLeadResponse>(TestApp.Json);
        var b = await (await client.PostAsJsonAsync("/api/v1/leads", new CreateLeadRequest { BusinessName = "Gamma Gym Surat", City = "Surat", Phone = "+91 98765 22222", ContactSourceUrl = "manual note", AllowDuplicate = true })).Content.ReadFromJsonAsync<LeadPilot.API.Controllers.LeadsController.CreateLeadResponse>(TestApp.Json);
        (await client.PostAsJsonAsync($"/api/v1/leads/{a!.Id}/merge", new MergeLeadRequest(b!.Id))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var merged = await client.GetFromJsonAsync<LeadDetail>($"/api/v1/leads/{a.Id}", TestApp.Json);
        merged!.Contacts.Should().Contain(c => c.Type == ContactType.Phone);
        (await client.GetAsync($"/api/v1/leads/{b.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

public class EmailApprovalTests : IAsyncLifetime
{
    private readonly TestApp _app = new();
    private HttpClient _client = null!;
    public async Task InitializeAsync()
    {
        await _app.InitAsync();
        _client = await _app.OwnerClientAsync();
    }
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    private async Task<EmailDraftDto> CreateDraftAsync(string email = "contact@prospect.example")
    {
        var lead = await (await _client.PostAsJsonAsync("/api/v1/leads", new CreateLeadRequest
        {
            BusinessName = "Prospect " + Guid.NewGuid().ToString("N")[..6], Email = email, ContactSourceUrl = "https://prospect.example/contact", AllowDuplicate = true
        })).Content.ReadFromJsonAsync<LeadPilot.API.Controllers.LeadsController.CreateLeadResponse>(TestApp.Json);
        var r = await _client.PostAsJsonAsync("/api/v1/emails", new CreateEmailDraftRequest(lead!.Id, null, null, "Improving enquiries", "Hi,\nI noticed your website has no enquiry form.\nRegards"));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<EmailDraftDto>(TestApp.Json))!;
    }

    private Task<HttpResponseMessage> Approve(EmailDraftDto d) => _client.PostAsJsonAsync($"/api/v1/emails/{d.Id}/approve", new ApproveEmailRequest(d.ContentHash, d.Version));

    private async Task ProcessOutboxAsync()
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessDueAsync(CancellationToken.None);
    }

    private async Task<EmailDraftDto> Reload(Guid id) => (await _client.GetFromJsonAsync<EmailDraftDto>($"/api/v1/emails/{id}", TestApp.Json))!;

    [Fact]
    public async Task Unapproved_email_cannot_be_sent()
    {
        var d = await CreateDraftAsync();
        d.Status.Should().Be(EmailStatus.PendingApproval);
        d.Body.Should().Contain("unsubscribe", "opt-out footer is part of the approved content");
        (await _client.PostAsync($"/api/v1/emails/{d.Id}/send", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _app.Email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Approval_must_match_viewed_content_hash()
    {
        var d = await CreateDraftAsync();
        var r = await _client.PostAsJsonAsync($"/api/v1/emails/{d.Id}/approve", new ApproveEmailRequest("0000", d.Version));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Editing_after_approval_invalidates_approval_and_blocks_send()
    {
        var d = await CreateDraftAsync();
        (await Approve(d)).StatusCode.Should().Be(HttpStatusCode.OK);
        var edited = await (await _client.PutAsJsonAsync($"/api/v1/emails/{d.Id}", new UpdateEmailDraftRequest(d.ToAddress, d.Subject, d.Body + " PS"))).Content.ReadFromJsonAsync<EmailDraftDto>(TestApp.Json);
        edited!.Status.Should().Be(EmailStatus.PendingApproval);
        edited.Version.Should().Be(2);
        edited.ContentHash.Should().NotBe(d.ContentHash);
        (await _client.PostAsync($"/api/v1/emails/{d.Id}/send", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _app.Email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Tampering_with_content_in_database_after_queueing_is_caught_before_send()
    {
        var d = await CreateDraftAsync();
        await Approve(d);
        (await _client.PostAsync($"/api/v1/emails/{d.Id}/send", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var draft = await db.EmailDrafts.FirstAsync(x => x.Id == d.Id);
            draft.Body = "Tampered body"; // hash not updated
            await db.SaveChangesAsync();
        }
        await ProcessOutboxAsync();
        _app.Email.Sent.Should().BeEmpty();
        (await Reload(d.Id)).Status.Should().Be(EmailStatus.PendingApproval);
    }

    [Fact]
    public async Task Approved_email_is_sent_once_and_crm_updated()
    {
        var d = await CreateDraftAsync();
        await Approve(d);
        (await _client.PostAsync($"/api/v1/emails/{d.Id}/send", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await _client.PostAsync($"/api/v1/emails/{d.Id}/send", null)).StatusCode.Should().Be(HttpStatusCode.Accepted, "a repeated request is idempotent");
        await ProcessOutboxAsync();
        await ProcessOutboxAsync();
        _app.Email.Sent.Should().HaveCount(1);
        var sent = await Reload(d.Id);
        sent.Status.Should().Be(EmailStatus.Sent);
        sent.GmailMessageId.Should().NotBeNull();
        (await _client.PostAsync($"/api/v1/emails/{d.Id}/send", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "an already-sent email cannot be sent again");

        var lead = await _client.GetFromJsonAsync<LeadDetail>($"/api/v1/leads/{d.LeadId}", TestApp.Json);
        lead!.Stage.Should().Be(PipelineStage.Contacted);
        lead.NextFollowUpAt.Should().NotBeNull();
        var audit = await _client.GetFromJsonAsync<List<EmailAuditDto>>($"/api/v1/emails/{d.Id}/audit", TestApp.Json);
        audit!.Select(a => a.Action).Should().ContainInOrder("created", "approved", "queued", "sending", "sent");
    }

    [Fact]
    public async Task Suppressed_recipient_cannot_be_emailed()
    {
        var d = await CreateDraftAsync("optout@prospect.example");
        await Approve(d);
        await _client.PostAsJsonAsync("/api/v1/compliance/suppressions", new AddSuppressionRequest(CommunicationChannel.Email, "OptOut@Prospect.example", "Requested", null));
        (await _client.PostAsync($"/api/v1/emails/{d.Id}/send", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _app.Email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Global_pause_blocks_sending()
    {
        var d = await CreateDraftAsync();
        await Approve(d);
        await _client.PostAsync("/api/v1/settings/outreach/pause?paused=true", null);
        var r = await _client.PostAsync($"/api/v1/emails/{d.Id}/send", null);
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("paused");
    }

    [Fact]
    public async Task Transient_failure_is_retried_but_unknown_outcome_is_never_resent()
    {
        var d = await CreateDraftAsync();
        await Approve(d);
        _app.Email.Results.Enqueue(new EmailSendResult(false, null, null, 503, "unavailable", false, Retryable: true));
        await _client.PostAsync($"/api/v1/emails/{d.Id}/send", null);
        await ProcessOutboxAsync();
        (await Reload(d.Id)).Status.Should().Be(EmailStatus.Queued);

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.EmailOutbox.FirstAsync(o => o.EmailDraftId == d.Id);
            row.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1); // make retry due now
            await db.SaveChangesAsync();
        }
        _app.Email.Results.Enqueue(new EmailSendResult(false, null, null, null, "response lost", OutcomeUnknown: true, Retryable: false));
        await ProcessOutboxAsync();
        (await Reload(d.Id)).Status.Should().Be(EmailStatus.OutcomeUnknown);
        await ProcessOutboxAsync();
        _app.Email.Sent.Should().HaveCount(2, "no blind resend after an unknown outcome");

        var resolved = await (await _client.PostAsJsonAsync($"/api/v1/emails/{d.Id}/resolve-unknown", new ResolveUnknownRequest(false, "Not in Sent folder"))).Content.ReadFromJsonAsync<EmailDraftDto>(TestApp.Json);
        resolved!.Status.Should().Be(EmailStatus.PendingApproval, "re-approval is required after reconciliation");
    }

    [Fact]
    public async Task Hourly_limit_is_enforced()
    {
        await _client.PutAsJsonAsync("/api/v1/settings/outreach", new OutreachSettings { HourlySendLimit = 1, DailySendLimit = 10 });
        var first = await CreateDraftAsync();
        await Approve(first);
        await _client.PostAsync($"/api/v1/emails/{first.Id}/send", null);
        await ProcessOutboxAsync();
        var second = await CreateDraftAsync();
        await Approve(second);
        var r = await _client.PostAsync($"/api/v1/emails/{second.Id}/send", null);
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("Hourly send limit");
    }
}

public class GmailOAuthTests : IAsyncLifetime
{
    private readonly TestApp _app = new();
    public Task InitializeAsync() => _app.InitAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Connect_url_uses_pkce_minimal_scope_and_state()
    {
        var client = await _app.OwnerClientAsync();
        var body = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/v1/integrations/google/connect");
        var url = body!["authorizationUrl"];
        url.Should().StartWith("https://accounts.google.com/o/oauth2/v2/auth?");
        url.Should().Contain("code_challenge_method=S256").And.Contain("state=").And.Contain(Uri.EscapeDataString("https://www.googleapis.com/auth/gmail.send"));
        url.Should().NotContain("mail.google.com").And.NotContain("gmail.readonly").And.NotContain("gmail.modify");
    }

    [Fact]
    public async Task State_is_single_use_and_unknown_state_is_rejected()
    {
        var client = await _app.OwnerClientAsync();
        var url = (await client.GetFromJsonAsync<Dictionary<string, string>>("/api/v1/integrations/google/connect"))!["authorizationUrl"];
        var state = Uri.UnescapeDataString(url.Split("state=")[1].Split('&')[0]);
        using var scope = _app.Services.CreateScope();
        var oauth = scope.ServiceProvider.GetRequiredService<GoogleOAuthService>();
        (await oauth.ValidateAndConsumeStateAsync(state, CancellationToken.None)).Should().NotBeNull();
        await oauth.Invoking(o => o.ValidateAndConsumeStateAsync(state, CancellationToken.None)).Should().ThrowAsync<ValidationFailedException>().WithMessage("*already used*");
        await oauth.Invoking(o => o.ValidateAndConsumeStateAsync("forged-state", CancellationToken.None)).Should().ThrowAsync<ValidationFailedException>();
    }

    [Fact]
    public async Task Callback_with_bad_state_shows_failure_page_without_connecting()
    {
        var r = await _app.CreateClient().GetAsync("/api/v1/integrations/google/callback?code=abc&state=forged");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).Should().Contain("connection failed");
        var client = await _app.OwnerClientAsync();
        var status = await client.GetFromJsonAsync<GoogleConnectionStatus>("/api/v1/integrations/google/status", TestApp.Json);
        status!.Connected.Should().BeFalse();
    }
}

public class AnalysisPipelineTests : IAsyncLifetime
{
    private readonly TestApp _app = new();
    public Task InitializeAsync() => _app.InitAsync();
    public Task DisposeAsync() { _app.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Lead_without_website_is_scored_with_breakdown_and_ai_mock_is_labelled()
    {
        var client = await _app.OwnerClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/v1/leads", new CreateLeadRequest { BusinessName = "Delta Traders", Phone = "+91 98765 33333", ContactSourceUrl = "visiting card" }))
            .Content.ReadFromJsonAsync<LeadPilot.API.Controllers.LeadsController.CreateLeadResponse>(TestApp.Json);
        using (var scope = _app.Services.CreateScope())
        {
            var pipeline = scope.ServiceProvider.GetRequiredService<LeadPilot.Application.Analysis.LeadAnalysisPipeline>();
            await pipeline.AnalyseWebsiteAsync(created!.Id, false, CancellationToken.None);
            await pipeline.RunAIAnalysisAsync(created.Id, CancellationToken.None);
        }
        var lead = await client.GetFromJsonAsync<LeadDetail>($"/api/v1/leads/{created!.Id}", TestApp.Json);
        lead!.Score.Should().NotBeNull();
        lead.LatestScore!.Components.Should().HaveCount(6);
        lead.Opportunities.Should().Contain(o => o.Code == "no_website");
        lead.LatestAIAnalysis!.IsMock.Should().BeTrue();
        lead.LatestAIAnalysis.BusinessSummary.Should().Contain("[MOCK]");
    }
}
