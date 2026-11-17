namespace LeadPilot.Domain;

public static class Roles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string User = "User";
}

public enum PipelineStage
{
    New = 0,
    Analysed = 1,
    Qualified = 2,
    PitchReady = 3,
    Contacted = 4,
    Replied = 5,
    Interested = 6,
    MeetingScheduled = 7,
    ProposalSent = 8,
    Negotiation = 9,
    Won = 10,
    Lost = 11,
    NotInterested = 12,
    FollowUpRequired = 13
}

public enum LeadPriority { Low = 0, NeedsReview = 1, Good = 2, High = 3 }

public enum ProcessStatus { NotStarted = 0, Queued = 1, Running = 2, Completed = 3, Failed = 4, Skipped = 5 }

public enum ContactType { Email = 0, Phone = 1, WhatsApp = 2, Website = 3, LinkedIn = 4, Other = 5 }

public enum DiscoveryJobStatus { Pending = 0, Running = 1, Paused = 2, Completed = 3, Failed = 4, Cancelled = 5 }

public enum DiscoveryResultOutcome { Created = 0, Duplicate = 1, Skipped = 2, Error = 3 }

public enum CheckResult { Pass = 0, Fail = 1, Warning = 2, Unknown = 3, NotApplicable = 4 }

public enum Confidence { Low = 0, Medium = 1, High = 2 }

public enum OpportunitySource { RuleEngine = 0, AI = 1, Manual = 2 }

public enum PitchType
{
    IntroEmail = 0,
    ShortIntro = 1,
    WhatsApp = 2,
    LinkedIn = 3,
    FollowUp = 4,
    ProposalOutline = 5,
    Quotation = 6,
    MeetingRequest = 7,
    WebsiteProposal = 8
}

public enum PitchLanguage { English = 0, Hindi = 1, Hinglish = 2 }

public enum EmailStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Queued = 3,
    Sending = 4,
    Sent = 5,
    Rejected = 6,
    Expired = 7,
    Failed = 8,
    OutcomeUnknown = 9
}

public enum OutboxStatus { Pending = 0, Processing = 1, Sent = 2, Failed = 3, OutcomeUnknown = 4, Cancelled = 5 }

public enum EmailAccountStatus { Connected = 0, Revoked = 1, Error = 2, Disconnected = 3 }

public enum CommunicationChannel { Email = 0, WhatsApp = 1, Phone = 2, LinkedIn = 3, Sms = 4 }

public enum ActivityType
{
    Note = 0,
    Call = 1,
    EmailSent = 2,
    EmailReceived = 3,
    StageChange = 4,
    Meeting = 5,
    Proposal = 6,
    FollowUp = 7,
    Analysis = 8,
    System = 9,
    WhatsApp = 10,
    LinkedIn = 11
}

public enum FollowUpStatus { Pending = 0, Completed = 1, Snoozed = 2, Cancelled = 3 }

public enum MeetingStatus { Scheduled = 0, Completed = 1, Cancelled = 2, NoShow = 3 }

public enum ProposalStatus { Draft = 0, Sent = 1, Accepted = 2, Rejected = 3, Expired = 4 }

public enum DealStatus { Open = 0, Won = 1, Lost = 2 }

public enum NotificationType
{
    DiscoveryCompleted = 0,
    HighPriorityLeads = 1,
    AnalysisCompleted = 2,
    ApprovalPending = 3,
    EmailFailed = 4,
    FollowUpDue = 5,
    ReplyRecorded = 6,
    JobError = 7,
    BudgetWarning = 8,
    General = 9
}

public enum DevicePlatform { Android = 0, Ios = 1, Web = 2 }
