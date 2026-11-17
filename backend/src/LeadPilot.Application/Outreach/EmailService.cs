using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using LeadPilot.Application.Crm;
using LeadPilot.Application.Notifications;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadPilot.Application.Outreach;

public sealed record CreateEmailDraftRequest(Guid LeadId, Guid? PitchDraftId, string? ToAddress, string? Subject, string? Body, Guid? FollowUpId = null);
public sealed record UpdateEmailDraftRequest(string ToAddress, string Subject, string Body, Guid? ConcurrencyStamp = null);
/// <summary>The client must echo the hash + version it displayed, proving the user approved exactly that content.</summary>
public sealed record ApproveEmailRequest(string ContentHash, int Version);
public sealed record RejectEmailRequest(string? Reason);
public sealed record ResolveUnknownRequest(bool WasSent, string? Note);

public sealed record EmailDraftDto(
    Guid Id, Guid LeadId, string LeadName, string ToAddress, string? ToName, string Subject, string Body, string ContentHash, int Version,
    EmailStatus Status, DateTime? ApprovedAt, DateTime? ApprovalExpiresAt, DateTime? SentAt, string? SenderAddress, string? GmailMessageId,
    string? GmailThreadId, string? LastError, string? RejectionReason, Guid ConcurrencyStamp, DateTime CreatedAt, DateTime UpdatedAt,
    bool RecipientSourceRecorded, string? RecipientSource, bool TestMode);

public sealed record EmailAuditDto(DateTime At, string Action, string? ContentHash, int? Version, string? Details);
public sealed record SendCheckDto(bool CanSend, IReadOnlyList<string> Problems);

public sealed class EmailService(
    IAppDbContext db,
    IConsentService consent,
    IEmailProvider emailProvider,
    ISettingsService settings,
    IJobScheduler jobs,
    NotificationService notifications,
    CrmService crm,
    ICurrentUser currentUser,
    IAuditService audit,
    IClock clock,
    IOptions<EmailOptions> emailOptions,
    ILogger<EmailService> logger)
{
    // =====================================================================
    // Drafts
    // =====================================================================

    public async Task<EmailDraftDto> CreateDraftAsync(CreateEmailDraftRequest r, CancellationToken ct)
    {
        var lead = await db.Leads.Include(l => l.Contacts).FirstOrDefaultAsync(l => l.Id == r.LeadId, ct) ?? throw new NotFoundException("Lead", r.LeadId);
        string subject, body;
        if (r.PitchDraftId is not null)
        {
            var pitch = await db.PitchDrafts.FirstOrDefaultAsync(p => p.Id == r.PitchDraftId && p.LeadId == r.LeadId, ct) ?? throw new NotFoundException("Pitch", r.PitchDraftId);
            subject = r.Subject ?? pitch.Subject ?? $"Regarding {lead.BusinessName}";
            body = r.Body ?? pitch.Body;
        }
        else
        {
            subject = r.Subject ?? throw new ValidationFailedException("Subject is required.");
            body = r.Body ?? throw new ValidationFailedException("Body is required.");
        }

        var to = Normalizer.NormalizeEmail(r.ToAddress) ?? lead.Contacts.Where(c => c.Type == ContactType.Email).OrderByDescending(c => c.IsPrimary).Select(c => c.NormalizedValue).FirstOrDefault()
                 ?? throw new ValidationFailedException("This lead has no source-recorded email address. Add one (with its source) before creating an email draft.");
        ValidateContent(to, subject, body);

        var outreach = await settings.GetAsync<OutreachSettings>(OutreachSettings.Key, ct);
        if (outreach.AppendOptOutFooter && !string.IsNullOrWhiteSpace(outreach.OptOutFooter) && !body.Contains(outreach.OptOutFooter))
            body = body.TrimEnd() + "\n\n" + outreach.OptOutFooter;

        var contact = lead.Contacts.FirstOrDefault(c => c.Type == ContactType.Email && c.NormalizedValue == to);
        var draft = new EmailDraft
        {
            OwnerUserId = currentUser.UserId ?? lead.OwnerUserId, LeadId = lead.Id, PitchDraftId = r.PitchDraftId, FollowUpId = r.FollowUpId,
            RecipientContactId = contact?.Id, ToAddress = to, Subject = subject.Trim(), Body = body.Trim(),
            Status = EmailStatus.PendingApproval
        };
        draft.ContentHash = EmailContentHasher.Compute(draft.ToAddress, draft.Subject, draft.Body);
        db.EmailDrafts.Add(draft);
        AddAudit(draft, "created", $"from pitch {r.PitchDraftId?.ToString() ?? "manual"}");
        await db.SaveChangesAsync(ct);

        await notifications.NotifyAsync(draft.OwnerUserId, NotificationType.ApprovalPending, "Email awaiting approval",
            "An email draft is waiting for your approval.", $"Draft to {lead.BusinessName} awaits approval.", $"/approvals/{draft.Id}", ct);
        return await GetAsync(draft.Id, ct);
    }

    private static void ValidateContent(string to, string subject, string body)
    {
        if (!Normalizer.IsValidEmailSyntax(to)) throw new ValidationFailedException("Recipient email address is invalid.");
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 200) throw new ValidationFailedException("Subject is required (max 200 characters).");
        if (subject.Contains('\r') || subject.Contains('\n')) throw new ValidationFailedException("Subject cannot contain line breaks.");
        if (string.IsNullOrWhiteSpace(body) || body.Length > 20000) throw new ValidationFailedException("Body is required (max 20,000 characters).");
    }

    public async Task<EmailDraftDto> UpdateDraftAsync(Guid id, UpdateEmailDraftRequest r, CancellationToken ct)
    {
        var draft = await db.EmailDrafts.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Email draft", id);
        if (r.ConcurrencyStamp is not null && r.ConcurrencyStamp != draft.ConcurrencyStamp)
            throw new ConflictException("The draft changed elsewhere. Reload and try again.", "concurrency_conflict");
        if (draft.Status is EmailStatus.Sending or EmailStatus.Sent or EmailStatus.OutcomeUnknown)
            throw new ConflictException($"A {draft.Status} email cannot be edited.", "not_editable");

        var to = Normalizer.NormalizeEmail(r.ToAddress) ?? "";
        ValidateContent(to, r.Subject, r.Body);
        var newHash = EmailContentHasher.Compute(to, r.Subject.Trim(), r.Body.Trim());
        if (newHash == draft.ContentHash) return await GetAsync(id, ct);

        var recipientChanged = !string.Equals(to, draft.ToAddress, StringComparison.OrdinalIgnoreCase);
        draft.ToAddress = to;
        draft.Subject = r.Subject.Trim();
        draft.Body = r.Body.Trim();
        draft.ContentHash = newHash;
        draft.Version++;
        if (recipientChanged)
        {
            var contact = await db.LeadContacts.FirstOrDefaultAsync(c => c.LeadId == draft.LeadId && c.Type == ContactType.Email && c.NormalizedValue == to, ct);
            draft.RecipientContactId = contact?.Id;
        }
        await InvalidateApprovalsAsync(draft, recipientChanged ? "recipient_changed" : "content_changed", ct);
        draft.Status = EmailStatus.PendingApproval;
        AddAudit(draft, "edited", recipientChanged ? "recipient changed; approval invalidated" : "content changed; approval invalidated");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task InvalidateApprovalsAsync(EmailDraft draft, string reason, CancellationToken ct)
    {
        var approvals = await db.EmailApprovals.Where(a => a.EmailDraftId == draft.Id && a.RevokedAt == null && a.ConsumedAt == null).ToListAsync(ct);
        foreach (var a in approvals) { a.RevokedAt = clock.UtcNow; a.RevokedReason = reason; }
        var pending = await db.EmailOutbox.Where(o => o.EmailDraftId == draft.Id && o.Status == OutboxStatus.Pending).ToListAsync(ct);
        foreach (var o in pending) { o.Status = OutboxStatus.Cancelled; o.LastError = reason; }
        draft.ApprovedAt = null;
    }

    // =====================================================================
    // Approval
    // =====================================================================

    public async Task<EmailDraftDto> ApproveAsync(Guid id, ApproveEmailRequest r, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var draft = await db.EmailDrafts.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Email draft", id);
        if (draft.Status is not (EmailStatus.PendingApproval or EmailStatus.Draft or EmailStatus.Expired or EmailStatus.Failed))
            throw new ConflictException($"A {draft.Status} email cannot be approved.", "invalid_state");
        if (!string.Equals(r.ContentHash, draft.ContentHash, StringComparison.OrdinalIgnoreCase) || r.Version != draft.Version)
            throw new ConflictException("The draft changed since you viewed it. Review the latest version before approving.", "content_changed");

        var outreach = await settings.GetAsync<OutreachSettings>(OutreachSettings.Key, ct);
        await InvalidateApprovalsAsync(draft, "superseded", ct);
        var approval = new EmailApproval
        {
            EmailDraftId = draft.Id, ApprovedByUserId = userId, ContentHash = draft.ContentHash, DraftVersion = draft.Version,
            ApprovedRecipient = draft.ToAddress, ApprovedAt = clock.UtcNow, ExpiresAt = clock.UtcNow.AddHours(Math.Max(1, outreach.ApprovalValidityHours))
        };
        db.EmailApprovals.Add(approval);
        draft.Status = EmailStatus.Approved;
        draft.ApprovedAt = approval.ApprovedAt;
        draft.LastError = null;
        AddAudit(draft, "approved", $"expires {approval.ExpiresAt:u}", userId);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("email.approve", nameof(EmailDraft), id.ToString(), $"hash={draft.ContentHash[..12]} v{draft.Version}", ct);
        return await GetAsync(id, ct);
    }

    public async Task<EmailDraftDto> RejectAsync(Guid id, RejectEmailRequest r, CancellationToken ct)
    {
        var draft = await db.EmailDrafts.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Email draft", id);
        if (draft.Status is EmailStatus.Sending or EmailStatus.Sent or EmailStatus.OutcomeUnknown)
            throw new ConflictException($"A {draft.Status} email cannot be rejected.", "invalid_state");
        await InvalidateApprovalsAsync(draft, "rejected", ct);
        draft.Status = EmailStatus.Rejected;
        draft.RejectionReason = r.Reason;
        AddAudit(draft, "rejected", r.Reason, currentUser.UserId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // =====================================================================
    // Send (user initiated) -> durable outbox
    // =====================================================================

    /// <summary>Full server-side validation. Never trusts client approval state.</summary>
    public async Task<(List<string> Problems, EmailApproval? Approval, EmailAccount? Account)> ValidateSendAsync(EmailDraft draft, bool forProcessing, CancellationToken ct)
    {
        var problems = new List<string>();
        var now = clock.UtcNow;
        var outreach = await settings.GetAsync<OutreachSettings>(OutreachSettings.Key, ct);

        if (outreach.PauseAllSending) problems.Add("Sending is globally paused (Settings → Outreach).");
        var allowedStatuses = forProcessing ? new[] { EmailStatus.Queued, EmailStatus.Sending } : [EmailStatus.Approved];
        if (!allowedStatuses.Contains(draft.Status)) problems.Add($"Draft status is {draft.Status}; it must be approved first.");

        var approval = await db.EmailApprovals.Where(a => a.EmailDraftId == draft.Id && a.RevokedAt == null)
            .OrderByDescending(a => a.ApprovedAt).FirstOrDefaultAsync(ct);
        if (approval is null) problems.Add("No valid approval record exists.");
        else
        {
            if (approval.ExpiresAt <= now) problems.Add("The approval has expired. Review and approve again.");
            if (!forProcessing && approval.ConsumedAt is not null) problems.Add("This approval was already used.");
            var currentHash = EmailContentHasher.Compute(draft.ToAddress, draft.Subject, draft.Body);
            if (currentHash != draft.ContentHash || approval.ContentHash != currentHash || approval.DraftVersion != draft.Version)
                problems.Add("Content changed after approval. Approve the current version.");
            if (!string.Equals(approval.ApprovedRecipient, draft.ToAddress, StringComparison.OrdinalIgnoreCase))
                problems.Add("Recipient changed after approval.");
        }

        if (!Normalizer.IsValidEmailSyntax(draft.ToAddress)) problems.Add("Recipient address is invalid.");
        var contact = await db.LeadContacts.AsNoTracking().FirstOrDefaultAsync(c => c.LeadId == draft.LeadId && c.Type == ContactType.Email && c.NormalizedValue == draft.ToAddress, ct);
        if (outreach.RequireSourceRecordedRecipient && (contact is null || !contact.SourceVerified))
            problems.Add("Recipient is not a source-recorded contact of this lead.");

        var lead = await db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == draft.LeadId, ct);
        if (lead is null) problems.Add("Lead no longer exists.");
        else
        {
            var eligibility = await consent.CheckEligibilityAsync(lead, CommunicationChannel.Email, draft.ToAddress, ct);
            problems.AddRange(eligibility.Reasons);
        }

        if (!forProcessing)
        {
            var already = await db.EmailOutbox.AnyAsync(o => o.EmailDraftId == draft.Id && (o.Status == OutboxStatus.Sent || o.Status == OutboxStatus.OutcomeUnknown), ct)
                          || draft.GmailMessageId is not null;
            if (already) problems.Add("This email has already been sent (or its outcome is being reconciled).");
        }

        var hourCount = await db.EmailDrafts.CountAsync(d => d.Id != draft.Id && d.SentAt != null && d.SentAt > now.AddHours(-1), ct);
        var dayCount = await db.EmailDrafts.CountAsync(d => d.Id != draft.Id && d.SentAt != null && d.SentAt > now.AddDays(-1), ct);
        if (hourCount >= outreach.HourlySendLimit) problems.Add($"Hourly send limit ({outreach.HourlySendLimit}) reached.");
        if (dayCount >= outreach.DailySendLimit) problems.Add($"Daily send limit ({outreach.DailySendLimit}) reached.");

        var account = await db.EmailAccounts.FirstOrDefaultAsync(a => a.OwnerUserId == draft.OwnerUserId && a.Status == EmailAccountStatus.Connected, ct);
        if (account is null && !emailOptions.Value.TestMode) problems.Add("Gmail is not connected (Settings → Integrations).");

        return (problems, approval, account);
    }

    public async Task<SendCheckDto> CheckSendAsync(Guid id, CancellationToken ct)
    {
        var draft = await db.EmailDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Email draft", id);
        var (problems, _, _) = await ValidateSendAsync(draft, false, ct);
        return new SendCheckDto(problems.Count == 0, problems);
    }

    public async Task<EmailDraftDto> SendAsync(Guid id, CancellationToken ct)
    {
        currentUser.RequireUserId();
        var draft = await db.EmailDrafts.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Email draft", id);

        // Idempotent: a repeated send request for an already-queued draft returns the current state.
        var existing = await db.EmailOutbox.FirstOrDefaultAsync(o => o.EmailDraftId == id && (o.Status == OutboxStatus.Pending || o.Status == OutboxStatus.Processing), ct);
        if (existing is not null) return await GetAsync(id, ct);

        var (problems, approval, _) = await ValidateSendAsync(draft, false, ct);
        if (problems.Count > 0)
        {
            AddAudit(draft, "send_refused", string.Join(" | ", problems), currentUser.UserId);
            await db.SaveChangesAsync(ct);
            throw new ValidationFailedException("Email cannot be sent: " + string.Join(" ", problems),
                new Dictionary<string, string[]> { ["send"] = problems.ToArray() });
        }

        var outbox = new EmailOutbox
        {
            EmailDraftId = draft.Id, EmailApprovalId = approval!.Id,
            IdempotencyKey = $"{draft.Id:N}:{approval.Id:N}:{draft.ContentHash[..16]}",
            Status = OutboxStatus.Pending, NextAttemptAt = clock.UtcNow
        };
        db.EmailOutbox.Add(outbox);
        draft.Status = EmailStatus.Queued;
        AddAudit(draft, "queued", $"idempotency {outbox.IdempotencyKey}", currentUser.UserId);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Unique IdempotencyKey violated: another request already queued this exact approval.
            logger.LogInformation(ex, "Duplicate send prevented for draft {DraftId}", id);
            throw new ConflictException("This email is already queued for sending.", "duplicate_send");
        }
        await audit.LogAsync("email.send_requested", nameof(EmailDraft), id.ToString(), null, ct);
        jobs.Enqueue<EmailOutboxProcessor>(p => p.ProcessOneAsync(outbox.Id, CancellationToken.None));
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Resolves an OutcomeUnknown email after the user checked their Gmail "Sent" folder. With the minimal
    /// gmail.send scope the server cannot read the mailbox, so reconciliation is a human decision.
    /// </summary>
    public async Task<EmailDraftDto> ResolveUnknownAsync(Guid id, ResolveUnknownRequest r, CancellationToken ct)
    {
        var draft = await db.EmailDrafts.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Email draft", id);
        if (draft.Status != EmailStatus.OutcomeUnknown) throw new ConflictException("Only emails with an unknown outcome can be reconciled.");
        var outbox = await db.EmailOutbox.Where(o => o.EmailDraftId == id && o.Status == OutboxStatus.OutcomeUnknown).ToListAsync(ct);
        if (r.WasSent)
        {
            draft.Status = EmailStatus.Sent;
            draft.SentAt ??= clock.UtcNow;
            foreach (var o in outbox) { o.Status = OutboxStatus.Sent; o.CompletedAt = clock.UtcNow; }
            await OnSentAsync(draft, ct);
            AddAudit(draft, "reconciled_sent", r.Note, currentUser.UserId);
        }
        else
        {
            draft.Status = EmailStatus.PendingApproval;
            foreach (var o in outbox) { o.Status = OutboxStatus.Failed; o.CompletedAt = clock.UtcNow; o.LastError = "Confirmed not sent by user"; }
            await InvalidateApprovalsAsync(draft, "reconciled_not_sent", ct);
            AddAudit(draft, "reconciled_not_sent", r.Note, currentUser.UserId);
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    internal async Task OnSentAsync(EmailDraft draft, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == draft.LeadId, ct);
        if (lead is null) return;
        lead.LastContactedAt = draft.SentAt;
        if (lead.Stage < PipelineStage.Contacted) lead.Stage = PipelineStage.Contacted;
        db.CrmActivities.Add(Activity.Create(lead.Id, ActivityType.EmailSent, $"Email sent: {draft.Subject}", $"To {draft.ToAddress}" + (emailOptions.Value.TestMode ? " (TEST MODE - not actually delivered)" : ""), draft.OwnerUserId));
        if (draft.FollowUpId is not null)
        {
            var f = await db.FollowUps.FirstOrDefaultAsync(x => x.Id == draft.FollowUpId, ct);
            if (f is not null) { f.Status = FollowUpStatus.Completed; f.CompletedAt = clock.UtcNow; }
        }
        else
        {
            await crm.ScheduleSequenceAsync(lead.Id, draft.SentAt ?? clock.UtcNow, ct);
        }
    }

    // =====================================================================
    // Queries
    // =====================================================================

    public async Task<PagedResult<EmailDraftDto>> ListAsync(EmailStatus[]? statuses, Guid? leadId, int page, int pageSize, CancellationToken ct)
    {
        var q = db.EmailDrafts.AsNoTracking().Where(d => (statuses == null || statuses.Length == 0 || statuses.Contains(d.Status)) && (leadId == null || d.LeadId == leadId));
        var total = await q.CountAsync(ct);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var ids = await q.OrderByDescending(d => d.UpdatedAt).Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).Select(d => d.Id).ToListAsync(ct);
        var items = new List<EmailDraftDto>();
        foreach (var id in ids) items.Add(await GetAsync(id, ct));
        return new PagedResult<EmailDraftDto>(items, page, pageSize, total);
    }

    public async Task<EmailDraftDto> GetAsync(Guid id, CancellationToken ct)
    {
        var d = await db.EmailDrafts.AsNoTracking().Include(x => x.Lead).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Email draft", id);
        var approval = await db.EmailApprovals.AsNoTracking().Where(a => a.EmailDraftId == id && a.RevokedAt == null).OrderByDescending(a => a.ApprovedAt).FirstOrDefaultAsync(ct);
        var contact = await db.LeadContacts.AsNoTracking().FirstOrDefaultAsync(c => c.LeadId == d.LeadId && c.Type == ContactType.Email && c.NormalizedValue == d.ToAddress, ct);
        return new EmailDraftDto(d.Id, d.LeadId, d.Lead?.BusinessName ?? "", d.ToAddress, d.ToName, d.Subject, d.Body, d.ContentHash, d.Version, d.Status,
            d.ApprovedAt, approval?.ExpiresAt, d.SentAt, d.SenderAddress, d.GmailMessageId, d.GmailThreadId, d.LastError, d.RejectionReason,
            d.ConcurrencyStamp, d.CreatedAt, d.UpdatedAt, contact?.SourceVerified ?? false,
            contact is null ? null : $"{contact.SourceProvider}{(contact.SourceUrl is null ? "" : " — " + contact.SourceUrl)}", emailOptions.Value.TestMode);
    }

    public async Task<List<EmailAuditDto>> AuditAsync(Guid id, CancellationToken ct) =>
        await db.EmailAuditLogs.AsNoTracking().Where(a => a.EmailDraftId == id).OrderBy(a => a.CreatedAt)
            .Select(a => new EmailAuditDto(a.CreatedAt, a.Action, a.ContentHash, a.DraftVersion, a.Details)).ToListAsync(ct);

    internal void AddAudit(EmailDraft d, string action, string? details, Guid? actor = null) =>
        db.EmailAuditLogs.Add(new EmailAuditLog { EmailDraftId = d.Id, ActorUserId = actor, Action = action, ContentHash = d.ContentHash, DraftVersion = d.Version, Details = details });

    internal IEmailProvider Provider => emailProvider;
    internal IAppDbContext Db => db;
    internal IClock Clock => clock;
    internal NotificationService Notifications => notifications;
}
