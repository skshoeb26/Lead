using LeadPilot.Domain.Common;

namespace LeadPilot.Domain.Entities;

public class EmailAccount : BaseEntity, IOwned
{
    public Guid OwnerUserId { get; set; }
    public string Provider { get; set; } = "google";
    public string EmailAddress { get; set; } = string.Empty;
    /// <summary>Refresh token encrypted with ASP.NET Data Protection. Never returned by the API.</summary>
    public string EncryptedRefreshToken { get; set; } = string.Empty;
    public string Scopes { get; set; } = string.Empty;
    public EmailAccountStatus Status { get; set; } = EmailAccountStatus.Connected;
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastTokenRefreshAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Short-lived OAuth state with PKCE verifier (encrypted). Single use.</summary>
public class OAuthState : BaseEntity
{
    public Guid UserId { get; set; }
    public string Provider { get; set; } = "google";
    public string StateHash { get; set; } = string.Empty;
    public string EncryptedCodeVerifier { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public class EmailDraft : BaseEntity, IConcurrencyAware, IOwned
{
    public Guid OwnerUserId { get; set; }
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public Guid? PitchDraftId { get; set; }
    public Guid? FollowUpId { get; set; }
    public Guid? RecipientContactId { get; set; }
    public string ToAddress { get; set; } = string.Empty;
    public string? ToName { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    /// <summary>SHA-256 over normalised recipient + subject + body.</summary>
    public string ContentHash { get; set; } = string.Empty;
    /// <summary>Incremented on every content change.</summary>
    public int Version { get; set; } = 1;
    public EmailStatus Status { get; set; } = EmailStatus.Draft;
    public DateTime? ApprovedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? SenderAddress { get; set; }
    public string? GmailMessageId { get; set; }
    public string? GmailThreadId { get; set; }
    public string? LastError { get; set; }
    public string? RejectionReason { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Approval bound to an exact content hash + version. Content changes revoke it.</summary>
public class EmailApproval : BaseEntity
{
    public Guid EmailDraftId { get; set; }
    public Guid ApprovedByUserId { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public int DraftVersion { get; set; }
    public string ApprovedRecipient { get; set; } = string.Empty;
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public DateTime? ConsumedAt { get; set; }

    public bool IsValid(DateTime now) => RevokedAt is null && ConsumedAt is null && ExpiresAt > now;
}

/// <summary>Durable outbox row. One row per approval; IdempotencyKey is unique so a draft cannot be queued twice.</summary>
public class EmailOutbox : BaseEntity, IConcurrencyAware
{
    public Guid EmailDraftId { get; set; }
    public Guid EmailApprovalId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? LockedUntil { get; set; }
    public string? LockedBy { get; set; }
    public string? LastError { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class EmailSendAttempt : BaseEntity
{
    public Guid EmailOutboxId { get; set; }
    public Guid EmailDraftId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public string Result { get; set; } = "started";
    public int? HttpStatus { get; set; }
    public string? GmailMessageId { get; set; }
    public string? GmailThreadId { get; set; }
    public string? Error { get; set; }
}

public class EmailAuditLog : BaseEntity
{
    public Guid EmailDraftId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public int? DraftVersion { get; set; }
    public string? Details { get; set; }
}

public class ConsentRecord : BaseEntity
{
    public Guid? LeadId { get; set; }
    public CommunicationChannel Channel { get; set; }
    public string ContactValue { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    /// <summary>How consent was obtained, e.g. "inbound enquiry form", "verbal on call 2026-01-04".</summary>
    public string Source { get; set; } = string.Empty;
    public string? Evidence { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
}

public class SuppressionEntry : BaseEntity
{
    public CommunicationChannel Channel { get; set; }
    public string Value { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? Source { get; set; }
    public Guid? LeadId { get; set; }
}
