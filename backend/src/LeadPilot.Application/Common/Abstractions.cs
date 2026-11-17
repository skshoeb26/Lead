using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeadPilot.Application.Common;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<SystemSetting> SystemSettings { get; }
    DbSet<ProviderConfiguration> ProviderConfigurations { get; }
    DbSet<UsageLog> UsageLogs { get; }
    DbSet<LeadCategory> LeadCategories { get; }
    DbSet<Lead> Leads { get; }
    DbSet<LeadContact> LeadContacts { get; }
    DbSet<LeadSource> LeadSources { get; }
    DbSet<DiscoveryJob> DiscoveryJobs { get; }
    DbSet<DiscoveryJobResult> DiscoveryJobResults { get; }
    DbSet<WebsiteAnalysis> WebsiteAnalyses { get; }
    DbSet<WebsiteAnalysisEvidence> WebsiteAnalysisEvidence { get; }
    DbSet<Opportunity> Opportunities { get; }
    DbSet<AIAnalysis> AIAnalyses { get; }
    DbSet<LeadScore> LeadScores { get; }
    DbSet<PitchDraft> PitchDrafts { get; }
    DbSet<EmailAccount> EmailAccounts { get; }
    DbSet<OAuthState> OAuthStates { get; }
    DbSet<EmailDraft> EmailDrafts { get; }
    DbSet<EmailApproval> EmailApprovals { get; }
    DbSet<EmailOutbox> EmailOutbox { get; }
    DbSet<EmailSendAttempt> EmailSendAttempts { get; }
    DbSet<EmailAuditLog> EmailAuditLogs { get; }
    DbSet<ConsentRecord> ConsentRecords { get; }
    DbSet<SuppressionEntry> SuppressionList { get; }
    DbSet<CrmActivity> CrmActivities { get; }
    DbSet<FollowUp> FollowUps { get; }
    DbSet<Meeting> Meetings { get; }
    DbSet<Proposal> Proposals { get; }
    DbSet<Deal> Deals { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<DeviceToken> DeviceTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Role { get; }
    string? IpAddress { get; }
    bool IsAuthenticated { get; }
    Guid RequireUserId();
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>Encrypts secrets at rest (OAuth refresh tokens, PKCE verifiers).</summary>
public interface ISecretProtector
{
    string Protect(string purpose, string plaintext);
    string Unprotect(string purpose, string ciphertext);
}

public interface IPasswordHasherService
{
    string Hash(string password);
    bool Verify(string hash, string password, out bool needsRehash);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateAccessToken(User user);
    string CreateRefreshTokenValue();
    string HashToken(string value);
}

/// <summary>Background job scheduling abstraction (Hangfire in production, inline/no-op in tests).</summary>
public interface IJobScheduler
{
    string Enqueue<T>(System.Linq.Expressions.Expression<Func<T, Task>> job);
    string Schedule<T>(System.Linq.Expressions.Expression<Func<T, Task>> job, TimeSpan delay);
    bool Delete(string jobId);
}

public interface IAuditService
{
    Task LogAsync(string action, string? entityType = null, string? entityId = null, string? details = null, CancellationToken ct = default);
}

public interface IUsageTracker
{
    Task RecordAsync(UsageLog log, CancellationToken ct = default);
    Task<decimal> GetSpendAsync(string provider, DateTime fromUtc, CancellationToken ct = default);
}
