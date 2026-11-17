using LeadPilot.Domain.Common;

namespace LeadPilot.Domain.Entities;

public class CrmActivity : BaseEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public ActivityType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public Guid? ActorUserId { get; set; }
    public string? MetadataJson { get; set; }
}

public class FollowUp : BaseEntity, IConcurrencyAware
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public DateTime DueAt { get; set; }
    public FollowUpStatus Status { get; set; } = FollowUpStatus.Pending;
    public CommunicationChannel Channel { get; set; } = CommunicationChannel.Email;
    public string? Note { get; set; }
    public int SequenceStep { get; set; }
    public Guid? EmailDraftId { get; set; }
    public DateTime? ReminderSentAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class Meeting : BaseEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public DateTime ScheduledAt { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public string? Location { get; set; }
    public string? Agenda { get; set; }
    public string? Outcome { get; set; }
    public MeetingStatus Status { get; set; } = MeetingStatus.Scheduled;
}

public class Proposal : BaseEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public string? Content { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Draft;
    public DateTime? SentAt { get; set; }
    public DateTime? ValidUntil { get; set; }
}

public class Deal : BaseEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal? ExpectedValue { get; set; }
    public decimal? ActualValue { get; set; }
    public string Currency { get; set; } = "INR";
    public DealStatus Status { get; set; } = DealStatus.Open;
    public DateTime? ClosedAt { get; set; }
    public string? LostReason { get; set; }
}

public class Notification : BaseEntity
{
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    /// <summary>In-app route, e.g. /leads/{id} or /approvals.</summary>
    public string? DeepLink { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? PushSentAt { get; set; }
    public string? PushError { get; set; }
}

public class DeviceToken : BaseEntity
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DevicePlatform Platform { get; set; } = DevicePlatform.Android;
    public string? DeviceName { get; set; }
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
