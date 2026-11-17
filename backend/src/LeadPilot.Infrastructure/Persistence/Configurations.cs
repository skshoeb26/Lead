using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeadPilot.Infrastructure.Persistence;

internal sealed class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> e)
    {
        e.HasIndex(x => x.NormalizedEmail).IsUnique();
        e.Property(x => x.Email).HasMaxLength(254).IsRequired();
        e.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
        e.Property(x => x.DisplayName).HasMaxLength(100);
        e.Property(x => x.Role).HasMaxLength(20);
        e.Property(x => x.PasswordHash).HasMaxLength(500);
    }
}

internal sealed class RefreshTokenConfig : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> e)
    {
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.HasIndex(x => x.FamilyId);
        e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        e.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        e.Property(x => x.CreatedByIp).HasMaxLength(64);
        e.Property(x => x.DeviceName).HasMaxLength(100);
        e.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> e)
    {
        e.HasIndex(x => x.CreatedAt);
        e.HasIndex(x => new { x.EntityType, x.EntityId });
        e.Property(x => x.Action).HasMaxLength(100);
        e.Property(x => x.EntityType).HasMaxLength(100);
        e.Property(x => x.EntityId).HasMaxLength(64);
        e.Property(x => x.IpAddress).HasMaxLength(64);
    }
}

internal sealed class SystemSettingConfig : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> e)
    {
        e.HasIndex(x => x.Key).IsUnique();
        e.Property(x => x.Key).HasMaxLength(100);
    }
}

internal sealed class ProviderConfigurationConfig : IEntityTypeConfiguration<ProviderConfiguration>
{
    public void Configure(EntityTypeBuilder<ProviderConfiguration> e)
    {
        e.HasIndex(x => x.Provider).IsUnique();
        e.Property(x => x.Provider).HasMaxLength(50);
    }
}

internal sealed class UsageLogConfig : IEntityTypeConfiguration<UsageLog>
{
    public void Configure(EntityTypeBuilder<UsageLog> e)
    {
        e.HasIndex(x => new { x.Provider, x.CreatedAt });
        e.Property(x => x.Provider).HasMaxLength(50);
        e.Property(x => x.Operation).HasMaxLength(100);
        e.Property(x => x.Model).HasMaxLength(100);
        e.Property(x => x.PromptVersion).HasMaxLength(50);
    }
}

internal sealed class LeadCategoryConfig : IEntityTypeConfiguration<LeadCategory>
{
    public void Configure(EntityTypeBuilder<LeadCategory> e)
    {
        e.HasIndex(x => x.Slug).IsUnique();
        e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        e.Property(x => x.Slug).HasMaxLength(100).IsRequired();
    }
}

internal sealed class LeadConfig : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> e)
    {
        e.HasQueryFilter(x => !x.IsDeleted);
        e.Property(x => x.BusinessName).HasMaxLength(300).IsRequired();
        e.Property(x => x.NormalizedName).HasMaxLength(300);
        e.Property(x => x.WebsiteUrl).HasMaxLength(500);
        e.Property(x => x.WebsiteDomain).HasMaxLength(255);
        e.Property(x => x.Phone).HasMaxLength(40);
        e.Property(x => x.NormalizedPhone).HasMaxLength(20);
        e.Property(x => x.Email).HasMaxLength(254);
        e.Property(x => x.City).HasMaxLength(100);
        e.Property(x => x.State).HasMaxLength(100);
        e.Property(x => x.District).HasMaxLength(100);
        e.Property(x => x.Locality).HasMaxLength(150);
        e.Property(x => x.PinCode).HasMaxLength(10);
        e.Property(x => x.SourceProvider).HasMaxLength(50);
        e.Property(x => x.ProviderPlaceId).HasMaxLength(255);
        e.Property(x => x.SourceUrl).HasMaxLength(1000);
        e.Property(x => x.BusinessStatus).HasMaxLength(50);
        e.Property(x => x.Tags).HasMaxLength(500);

        e.HasIndex(x => new { x.SourceProvider, x.ProviderPlaceId });
        e.HasIndex(x => x.WebsiteDomain);
        e.HasIndex(x => x.NormalizedPhone);
        e.HasIndex(x => x.NormalizedName);
        e.HasIndex(x => new { x.State, x.City });
        e.HasIndex(x => x.PinCode);
        e.HasIndex(x => x.Score);
        e.HasIndex(x => x.Stage);
        e.HasIndex(x => x.DiscoveredAt);
        e.HasIndex(x => x.NextFollowUpAt);
        e.HasIndex(x => x.OwnerUserId);

        e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        e.HasMany(x => x.Contacts).WithOne(c => c.Lead).HasForeignKey(c => c.LeadId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Sources).WithOne(c => c.Lead).HasForeignKey(c => c.LeadId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Opportunities).WithOne(c => c.Lead).HasForeignKey(c => c.LeadId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LeadContactConfig : IEntityTypeConfiguration<LeadContact>
{
    public void Configure(EntityTypeBuilder<LeadContact> e)
    {
        e.HasQueryFilter(x => x.Lead != null && !x.Lead.IsDeleted);
        e.HasIndex(x => new { x.LeadId, x.Type, x.NormalizedValue }).IsUnique();
        e.HasIndex(x => x.NormalizedValue);
        e.Property(x => x.Value).HasMaxLength(500).IsRequired();
        e.Property(x => x.NormalizedValue).HasMaxLength(500);
        e.Property(x => x.Label).HasMaxLength(100);
        e.Property(x => x.SourceProvider).HasMaxLength(50);
        e.Property(x => x.SourceUrl).HasMaxLength(1000);
    }
}

internal sealed class LeadSourceConfig : IEntityTypeConfiguration<LeadSource>
{
    public void Configure(EntityTypeBuilder<LeadSource> e)
    {
        e.HasQueryFilter(x => x.Lead != null && !x.Lead.IsDeleted);
        e.HasIndex(x => new { x.Provider, x.ExternalId });
        e.Property(x => x.Provider).HasMaxLength(50);
        e.Property(x => x.ExternalId).HasMaxLength(255);
        e.Property(x => x.SourceUrl).HasMaxLength(1000);
        e.Property(x => x.Attribution).HasMaxLength(300);
    }
}

internal sealed class DiscoveryJobConfig : IEntityTypeConfiguration<DiscoveryJob>
{
    public void Configure(EntityTypeBuilder<DiscoveryJob> e)
    {
        e.HasIndex(x => new { x.Status, x.CreatedAt });
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.Provider).HasMaxLength(50);
        e.Property(x => x.HangfireJobId).HasMaxLength(100);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DiscoveryJobResultConfig : IEntityTypeConfiguration<DiscoveryJobResult>
{
    public void Configure(EntityTypeBuilder<DiscoveryJobResult> e)
    {
        e.HasIndex(x => x.DiscoveryJobId);
        e.Property(x => x.BusinessName).HasMaxLength(300);
        e.Property(x => x.ExternalId).HasMaxLength(255);
        e.HasOne<DiscoveryJob>().WithMany().HasForeignKey(x => x.DiscoveryJobId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WebsiteAnalysisConfig : IEntityTypeConfiguration<WebsiteAnalysis>
{
    public void Configure(EntityTypeBuilder<WebsiteAnalysis> e)
    {
        e.HasQueryFilter(x => x.Lead != null && !x.Lead.IsDeleted);
        e.HasIndex(x => new { x.LeadId, x.CreatedAt });
        e.Property(x => x.RequestedUrl).HasMaxLength(1000);
        e.Property(x => x.FinalUrl).HasMaxLength(1000);
        e.Property(x => x.Title).HasMaxLength(500);
        e.Property(x => x.MetaDescription).HasMaxLength(1000);
        e.Property(x => x.SiteTypeGuess).HasMaxLength(50);
        e.Property(x => x.DetectedGenerator).HasMaxLength(200);
        e.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Evidence).WithOne().HasForeignKey(x => x.WebsiteAnalysisId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EvidenceConfig : IEntityTypeConfiguration<WebsiteAnalysisEvidence>
{
    public void Configure(EntityTypeBuilder<WebsiteAnalysisEvidence> e)
    {
        e.Property(x => x.Check).HasMaxLength(100);
        e.Property(x => x.Observation).HasMaxLength(2000);
        e.Property(x => x.PageUrl).HasMaxLength(1000);
    }
}

internal sealed class OpportunityConfig : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> e)
    {
        e.HasQueryFilter(x => x.Lead != null && !x.Lead.IsDeleted);
        e.HasIndex(x => x.LeadId);
        e.Property(x => x.Code).HasMaxLength(60);
        e.Property(x => x.Title).HasMaxLength(300);
        e.Property(x => x.RecommendedService).HasMaxLength(300);
    }
}

internal sealed class AIAnalysisConfig : IEntityTypeConfiguration<AIAnalysis>
{
    public void Configure(EntityTypeBuilder<AIAnalysis> e)
    {
        e.HasQueryFilter(x => x.Lead != null && !x.Lead.IsDeleted);
        e.HasIndex(x => new { x.LeadId, x.CreatedAt });
        e.Property(x => x.Provider).HasMaxLength(50);
        e.Property(x => x.Model).HasMaxLength(100);
        e.Property(x => x.PromptVersion).HasMaxLength(50);
        e.Property(x => x.InputHash).HasMaxLength(64);
        e.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LeadScoreConfig : IEntityTypeConfiguration<LeadScore>
{
    public void Configure(EntityTypeBuilder<LeadScore> e)
    {
        e.HasIndex(x => new { x.LeadId, x.CreatedAt });
        e.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PitchDraftConfig : IEntityTypeConfiguration<PitchDraft>
{
    public void Configure(EntityTypeBuilder<PitchDraft> e)
    {
        e.HasQueryFilter(x => x.Lead != null && !x.Lead.IsDeleted);
        e.HasIndex(x => new { x.LeadId, x.CreatedAt });
        e.Property(x => x.Subject).HasMaxLength(200);
        e.Property(x => x.Provider).HasMaxLength(50);
        e.Property(x => x.Model).HasMaxLength(100);
        e.Property(x => x.PromptVersion).HasMaxLength(50);
        e.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmailAccountConfig : IEntityTypeConfiguration<EmailAccount>
{
    public void Configure(EntityTypeBuilder<EmailAccount> e)
    {
        e.HasIndex(x => new { x.OwnerUserId, x.Provider, x.EmailAddress }).IsUnique();
        e.Property(x => x.Provider).HasMaxLength(30);
        e.Property(x => x.EmailAddress).HasMaxLength(254);
        e.Property(x => x.Scopes).HasMaxLength(1000);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OAuthStateConfig : IEntityTypeConfiguration<OAuthState>
{
    public void Configure(EntityTypeBuilder<OAuthState> e)
    {
        e.HasIndex(x => x.StateHash).IsUnique();
        e.Property(x => x.StateHash).HasMaxLength(128);
        e.Property(x => x.RedirectUri).HasMaxLength(500);
    }
}

internal sealed class EmailDraftConfig : IEntityTypeConfiguration<EmailDraft>
{
    public void Configure(EntityTypeBuilder<EmailDraft> e)
    {
        e.HasQueryFilter(x => x.Lead != null && !x.Lead.IsDeleted);
        e.HasIndex(x => new { x.Status, x.UpdatedAt });
        e.HasIndex(x => x.LeadId);
        e.HasIndex(x => x.SentAt);
        e.Property(x => x.ToAddress).HasMaxLength(254).IsRequired();
        e.Property(x => x.ToName).HasMaxLength(200);
        e.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        e.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        e.Property(x => x.SenderAddress).HasMaxLength(254);
        e.Property(x => x.GmailMessageId).HasMaxLength(100);
        e.Property(x => x.GmailThreadId).HasMaxLength(100);
        e.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmailApprovalConfig : IEntityTypeConfiguration<EmailApproval>
{
    public void Configure(EntityTypeBuilder<EmailApproval> e)
    {
        e.HasIndex(x => x.EmailDraftId);
        e.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        e.Property(x => x.ApprovedRecipient).HasMaxLength(254);
        e.HasOne<EmailDraft>().WithMany().HasForeignKey(x => x.EmailDraftId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmailOutboxConfig : IEntityTypeConfiguration<EmailOutbox>
{
    public void Configure(EntityTypeBuilder<EmailOutbox> e)
    {
        e.HasIndex(x => x.IdempotencyKey).IsUnique();
        e.HasIndex(x => x.EmailApprovalId).IsUnique();
        e.HasIndex(x => new { x.Status, x.NextAttemptAt });
        e.Property(x => x.IdempotencyKey).HasMaxLength(120);
        e.Property(x => x.LockedBy).HasMaxLength(100);
        e.HasOne<EmailDraft>().WithMany().HasForeignKey(x => x.EmailDraftId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmailSendAttemptConfig : IEntityTypeConfiguration<EmailSendAttempt>
{
    public void Configure(EntityTypeBuilder<EmailSendAttempt> e)
    {
        e.HasIndex(x => x.EmailOutboxId);
        e.Property(x => x.Result).HasMaxLength(30);
        e.Property(x => x.GmailMessageId).HasMaxLength(100);
        e.Property(x => x.GmailThreadId).HasMaxLength(100);
        e.HasOne<EmailOutbox>().WithMany().HasForeignKey(x => x.EmailOutboxId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmailAuditLogConfig : IEntityTypeConfiguration<EmailAuditLog>
{
    public void Configure(EntityTypeBuilder<EmailAuditLog> e)
    {
        e.HasIndex(x => new { x.EmailDraftId, x.CreatedAt });
        e.Property(x => x.Action).HasMaxLength(50);
        e.Property(x => x.ContentHash).HasMaxLength(64);
        e.HasOne<EmailDraft>().WithMany().HasForeignKey(x => x.EmailDraftId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConsentRecordConfig : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> e)
    {
        e.HasIndex(x => new { x.Channel, x.NormalizedValue });
        e.Property(x => x.ContactValue).HasMaxLength(254);
        e.Property(x => x.NormalizedValue).HasMaxLength(254);
        e.Property(x => x.Purpose).HasMaxLength(200);
        e.Property(x => x.Source).HasMaxLength(500);
    }
}

internal sealed class SuppressionConfig : IEntityTypeConfiguration<SuppressionEntry>
{
    public void Configure(EntityTypeBuilder<SuppressionEntry> e)
    {
        e.HasIndex(x => new { x.Channel, x.NormalizedValue }).IsUnique();
        e.Property(x => x.Value).HasMaxLength(254);
        e.Property(x => x.NormalizedValue).HasMaxLength(254);
        e.Property(x => x.Reason).HasMaxLength(300);
        e.Property(x => x.Source).HasMaxLength(100);
    }
}

internal sealed class CrmActivityConfig : IEntityTypeConfiguration<CrmActivity>
{
    public void Configure(EntityTypeBuilder<CrmActivity> e)
    {
        e.HasQueryFilter(x => x.Lead != null && !x.Lead.IsDeleted);
        e.HasIndex(x => new { x.LeadId, x.OccurredAt });
        e.HasIndex(x => new { x.Type, x.OccurredAt });
        e.Property(x => x.Title).HasMaxLength(300);
        e.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FollowUpConfig : IEntityTypeConfiguration<FollowUp>
{
    public void Configure(EntityTypeBuilder<FollowUp> e)
    {
        e.HasQueryFilter(x => x.Lead != null && !x.Lead.IsDeleted);
        e.HasIndex(x => new { x.Status, x.DueAt });
        e.HasIndex(x => x.LeadId);
        e.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MeetingConfig : IEntityTypeConfiguration<Meeting>
{
    public void Configure(EntityTypeBuilder<Meeting> e)
    {
        e.HasQueryFilter(x => x.Lead != null && !x.Lead.IsDeleted);
        e.HasIndex(x => x.ScheduledAt);
        e.Property(x => x.Location).HasMaxLength(500);
        e.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProposalConfig : IEntityTypeConfiguration<Proposal>
{
    public void Configure(EntityTypeBuilder<Proposal> e)
    {
        e.HasQueryFilter(x => x.Lead != null && !x.Lead.IsDeleted);
        e.Property(x => x.Title).HasMaxLength(300);
        e.Property(x => x.Currency).HasMaxLength(3);
        e.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DealConfig : IEntityTypeConfiguration<Deal>
{
    public void Configure(EntityTypeBuilder<Deal> e)
    {
        e.HasQueryFilter(x => x.Lead != null && !x.Lead.IsDeleted);
        e.Property(x => x.Title).HasMaxLength(300);
        e.Property(x => x.Currency).HasMaxLength(3);
        e.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class NotificationConfig : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> e)
    {
        e.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
        e.Property(x => x.Title).HasMaxLength(200);
        e.Property(x => x.Body).HasMaxLength(1000);
        e.Property(x => x.DeepLink).HasMaxLength(300);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DeviceTokenConfig : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> e)
    {
        e.HasIndex(x => x.Token).IsUnique();
        e.Property(x => x.Token).HasMaxLength(4096);
        e.Property(x => x.DeviceName).HasMaxLength(100);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
