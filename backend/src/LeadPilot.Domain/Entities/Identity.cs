using LeadPilot.Domain.Common;

namespace LeadPilot.Domain.Entities;

public class User : BaseEntity, IConcurrencyAware
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = Roles.Owner;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<RefreshToken> RefreshTokens { get; set; } = [];
}

/// <summary>Refresh tokens are stored hashed. Rotation chains share a FamilyId so reuse can revoke the family.</summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public Guid FamilyId { get; set; } = Guid.NewGuid();
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }
    public string? DeviceName { get; set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}

public class AuditLog : BaseEntity
{
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    /// <summary>Free text / JSON. Never contains secrets, tokens or passwords.</summary>
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
}

public class SystemSetting : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = "{}";
}

/// <summary>Non-secret provider configuration (quotas, budgets, enable flags). Secrets live in environment variables.</summary>
public class ProviderConfiguration : BaseEntity
{
    public string Provider { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int? DailyRequestQuota { get; set; }
    public decimal? DailyBudgetUsd { get; set; }
    public decimal? MonthlyBudgetUsd { get; set; }
    public int? RateLimitPerMinute { get; set; }
    public string SettingsJson { get; set; } = "{}";
}

public class UsageLog : BaseEntity
{
    /// <summary>claude, google_places, pagespeed, gmail, fcm ...</summary>
    public string Provider { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? JobId { get; set; }
    public int Units { get; set; } = 1;
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public bool Success { get; set; } = true;
    public int DurationMs { get; set; }
    public string? Error { get; set; }
}
