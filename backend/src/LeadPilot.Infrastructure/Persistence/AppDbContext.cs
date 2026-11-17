using LeadPilot.Application.Common;
using LeadPilot.Domain.Common;
using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeadPilot.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<ProviderConfiguration> ProviderConfigurations => Set<ProviderConfiguration>();
    public DbSet<UsageLog> UsageLogs => Set<UsageLog>();
    public DbSet<LeadCategory> LeadCategories => Set<LeadCategory>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadContact> LeadContacts => Set<LeadContact>();
    public DbSet<LeadSource> LeadSources => Set<LeadSource>();
    public DbSet<DiscoveryJob> DiscoveryJobs => Set<DiscoveryJob>();
    public DbSet<DiscoveryJobResult> DiscoveryJobResults => Set<DiscoveryJobResult>();
    public DbSet<WebsiteAnalysis> WebsiteAnalyses => Set<WebsiteAnalysis>();
    public DbSet<WebsiteAnalysisEvidence> WebsiteAnalysisEvidence => Set<WebsiteAnalysisEvidence>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<AIAnalysis> AIAnalyses => Set<AIAnalysis>();
    public DbSet<LeadScore> LeadScores => Set<LeadScore>();
    public DbSet<PitchDraft> PitchDrafts => Set<PitchDraft>();
    public DbSet<EmailAccount> EmailAccounts => Set<EmailAccount>();
    public DbSet<OAuthState> OAuthStates => Set<OAuthState>();
    public DbSet<EmailDraft> EmailDrafts => Set<EmailDraft>();
    public DbSet<EmailApproval> EmailApprovals => Set<EmailApproval>();
    public DbSet<EmailOutbox> EmailOutbox => Set<EmailOutbox>();
    public DbSet<EmailSendAttempt> EmailSendAttempts => Set<EmailSendAttempt>();
    public DbSet<EmailAuditLog> EmailAuditLogs => Set<EmailAuditLog>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<SuppressionEntry> SuppressionList => Set<SuppressionEntry>();
    public DbSet<CrmActivity> CrmActivities => Set<CrmActivity>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<Proposal> Proposals => Set<Proposal>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entity in b.Model.GetEntityTypes())
        {
            if (typeof(IConcurrencyAware).IsAssignableFrom(entity.ClrType))
                b.Entity(entity.ClrType).Property(nameof(IConcurrencyAware.ConcurrencyStamp)).IsConcurrencyToken();

            // Store every DateTime as UTC (timestamp with time zone on PostgreSQL).
            foreach (var p in entity.GetProperties().Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
                p.SetValueConverter(p.ClrType == typeof(DateTime) ? UtcConverters.Required : UtcConverters.Optional);

            foreach (var p in entity.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            { p.SetPrecision(18); p.SetScale(6); }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = entry.Entity.CreatedAt == default ? now : entry.Entity.CreatedAt;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                if (entry.Entity is IConcurrencyAware ca) ca.ConcurrencyStamp = Guid.NewGuid();
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}

internal static class UtcConverters
{
    public static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime> Required =
        new(v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v.ToUniversalTime(), DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    public static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?> Optional =
        new(v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v.Value.ToUniversalTime(), DateTimeKind.Utc)) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
