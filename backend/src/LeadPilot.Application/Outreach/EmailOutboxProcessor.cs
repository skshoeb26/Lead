using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadPilot.Application.Outreach;

/// <summary>
/// Processes the durable email outbox. Guarantees:
/// - a row is claimed with an optimistic-concurrency lock, so two workers never send the same row;
/// - every send is re-validated immediately before the provider call (approval, hash, suppression, limits, pause);
/// - a send attempt is persisted as "started" BEFORE calling Gmail; if the result is lost, the row becomes
///   OutcomeUnknown and is never retried automatically (no blind resends);
/// - only failures where Gmail definitely did not accept the message are retried, with backoff.
/// </summary>
public sealed class EmailOutboxProcessor(
    EmailService emails,
    ISettingsService settings,
    IOptions<EmailOptions> emailOptions,
    ILogger<EmailOutboxProcessor> logger)
{
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);
    private readonly string _workerId = Truncate($"{Environment.MachineName}:{Guid.NewGuid():N}", 90);

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] : s;

    public async Task ProcessDueAsync(CancellationToken ct)
    {
        var db = emails.Db;
        var now = emails.Clock.UtcNow;
        var due = await db.EmailOutbox.AsNoTracking()
            .Where(o => o.Status == OutboxStatus.Pending && o.NextAttemptAt <= now && (o.LockedUntil == null || o.LockedUntil < now))
            .OrderBy(o => o.NextAttemptAt).Select(o => o.Id).Take(20).ToListAsync(ct);
        foreach (var id in due) await ProcessOneAsync(id, ct);

        // Recover rows stuck in Processing (worker crashed mid-claim BEFORE an attempt started).
        var stuck = await db.EmailOutbox.Where(o => o.Status == OutboxStatus.Processing && o.LockedUntil < now).ToListAsync(ct);
        foreach (var o in stuck)
        {
            var attemptStarted = await db.EmailSendAttempts.AnyAsync(a => a.EmailOutboxId == o.Id && a.Result == "started", ct);
            if (attemptStarted)
            {
                o.Status = OutboxStatus.OutcomeUnknown;
                o.LastError = "Worker stopped during send; outcome unknown.";
                var d = await db.EmailDrafts.FirstOrDefaultAsync(x => x.Id == o.EmailDraftId, ct);
                if (d is not null) { d.Status = EmailStatus.OutcomeUnknown; emails.AddAudit(d, "outcome_unknown", o.LastError); }
            }
            else
            {
                o.Status = OutboxStatus.Pending;
                o.LockedUntil = null;
            }
        }
        if (stuck.Count > 0) await db.SaveChangesAsync(ct);
    }

    public async Task ProcessOneAsync(Guid outboxId, CancellationToken ct)
    {
        var db = emails.Db;
        var clock = emails.Clock;
        var outbox = await db.EmailOutbox.FirstOrDefaultAsync(o => o.Id == outboxId, ct);
        if (outbox is null || outbox.Status != OutboxStatus.Pending) return;
        if (outbox.LockedUntil is not null && outbox.LockedUntil > clock.UtcNow) return;

        // ---- claim (optimistic concurrency on ConcurrencyStamp)
        outbox.Status = OutboxStatus.Processing;
        outbox.LockedUntil = clock.UtcNow.Add(LockDuration);
        outbox.LockedBy = _workerId;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("Outbox row {OutboxId} claimed by another worker", outboxId);
            return;
        }

        var draft = await db.EmailDrafts.FirstAsync(d => d.Id == outbox.EmailDraftId, ct);

        // ---- re-validate right before sending
        var (problems, approval, account) = await emails.ValidateSendAsync(draft, forProcessing: true, ct);
        if (approval is null || approval.Id != outbox.EmailApprovalId) problems.Add("Approval changed since the email was queued.");
        if (problems.Count > 0)
        {
            outbox.Status = OutboxStatus.Cancelled;
            outbox.LastError = string.Join(" | ", problems);
            outbox.CompletedAt = clock.UtcNow;
            var pausedOrLimited = problems.All(p => p.Contains("paused") || p.Contains("limit"));
            draft.Status = pausedOrLimited ? EmailStatus.Approved : EmailStatus.PendingApproval;
            draft.LastError = outbox.LastError;
            emails.AddAudit(draft, "send_blocked", outbox.LastError);
            await db.SaveChangesAsync(ct);
            return;
        }

        var owner = await settings.GetAsync<OwnerProfile>(OwnerProfile.Key, ct);
        account ??= new EmailAccount { EmailAddress = owner.Email ?? "test-mode@localhost", OwnerUserId = draft.OwnerUserId };
        var from = string.IsNullOrWhiteSpace(owner.Name) ? account.EmailAddress : $"{owner.Name} <{account.EmailAddress}>";

        // ---- persist attempt BEFORE calling the provider
        outbox.Attempts++;
        var attempt = new EmailSendAttempt { EmailOutboxId = outbox.Id, EmailDraftId = draft.Id, AttemptNumber = outbox.Attempts, StartedAt = clock.UtcNow };
        db.EmailSendAttempts.Add(attempt);
        draft.Status = EmailStatus.Sending;
        approval!.ConsumedAt = clock.UtcNow;
        emails.AddAudit(draft, "sending", $"attempt {outbox.Attempts}{(emailOptions.Value.TestMode ? " [TEST MODE]" : "")}");
        await db.SaveChangesAsync(ct);

        EmailSendResult result;
        try
        {
            result = await emails.Provider.SendAsync(account, new OutgoingEmail(from, draft.ToAddress, draft.ToName, draft.Subject, draft.Body,
                account.EmailAddress, $"<mailto:{account.EmailAddress}?subject=unsubscribe>"), ct);
        }
        catch (Exception ex)
        {
            // Anything unexpected after the request may have left the machine: treat as unknown.
            logger.LogError(ex, "Unexpected error sending draft {DraftId}", draft.Id);
            result = new EmailSendResult(false, null, null, null, ex.Message, OutcomeUnknown: true, Retryable: false);
        }

        attempt.FinishedAt = clock.UtcNow;
        attempt.HttpStatus = result.HttpStatus;
        attempt.GmailMessageId = result.MessageId;
        attempt.GmailThreadId = result.ThreadId;
        attempt.Error = result.Error;
        outbox.LockedUntil = null;

        if (result.Accepted)
        {
            attempt.Result = "accepted";
            outbox.Status = OutboxStatus.Sent;
            outbox.CompletedAt = clock.UtcNow;
            draft.Status = EmailStatus.Sent;
            draft.SentAt = clock.UtcNow;
            draft.SenderAddress = account.EmailAddress;
            draft.GmailMessageId = result.MessageId;
            draft.GmailThreadId = result.ThreadId;
            draft.LastError = null;
            emails.AddAudit(draft, "sent", $"Gmail accepted message {result.MessageId} (acceptance is not proof of inbox delivery or reading)");
            await emails.OnSentAsync(draft, ct);
        }
        else if (result.OutcomeUnknown)
        {
            attempt.Result = "unknown";
            outbox.Status = OutboxStatus.OutcomeUnknown;
            outbox.LastError = result.Error;
            draft.Status = EmailStatus.OutcomeUnknown;
            draft.LastError = "Gmail may or may not have accepted this email. Check your Gmail Sent folder, then mark it as sent or not sent.";
            emails.AddAudit(draft, "outcome_unknown", result.Error);
            await emails.Notifications.NotifyAsync(draft.OwnerUserId, NotificationType.EmailFailed, "Email outcome unknown",
                "An email needs reconciliation. Tap to review.", null, $"/approvals/{draft.Id}", ct);
        }
        else if (result.Retryable && outbox.Attempts < outbox.MaxAttempts)
        {
            attempt.Result = "retry";
            outbox.Status = OutboxStatus.Pending;
            outbox.NextAttemptAt = clock.UtcNow.AddMinutes(Math.Pow(2, outbox.Attempts) * 2);
            outbox.LastError = result.Error;
            draft.Status = EmailStatus.Queued;
            draft.LastError = $"Temporary failure, retrying: {result.Error}";
            // Re-arm the approval for the retry; content is unchanged (hash re-validated before each attempt).
            approval.ConsumedAt = null;
            emails.AddAudit(draft, "retry_scheduled", result.Error);
        }
        else
        {
            attempt.Result = "failed";
            outbox.Status = OutboxStatus.Failed;
            outbox.CompletedAt = clock.UtcNow;
            outbox.LastError = result.Error;
            draft.Status = EmailStatus.Failed;
            draft.LastError = result.Error;
            emails.AddAudit(draft, "failed", result.Error);
            if (result.HttpStatus is 401 or 403 && account.Id != Guid.Empty)
            {
                var acc = await db.EmailAccounts.FirstOrDefaultAsync(a => a.Id == account.Id, ct);
                if (acc is not null) { acc.Status = EmailAccountStatus.Error; acc.LastError = result.Error; }
            }
            await emails.Notifications.NotifyAsync(draft.OwnerUserId, NotificationType.EmailFailed, "Email failed",
                "An email could not be sent. Tap to review.", null, $"/approvals/{draft.Id}", ct);
        }
        await db.SaveChangesAsync(ct);
    }
}
