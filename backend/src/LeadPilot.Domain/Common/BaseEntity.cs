namespace LeadPilot.Domain.Common;

/// <summary>Base type for all persisted entities. All timestamps are UTC.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Entities that use optimistic concurrency. The stamp is regenerated on every save.</summary>
public interface IConcurrencyAware
{
    Guid ConcurrencyStamp { get; set; }
}

/// <summary>Entities that are soft-deleted (hidden by a global query filter) rather than removed.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}

/// <summary>Entities owned by a single user (ready for multi-user support).</summary>
public interface IOwned
{
    Guid OwnerUserId { get; set; }
}
