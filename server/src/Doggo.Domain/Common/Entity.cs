namespace Doggo.Domain.Common;

/// <summary>
/// Base for all persisted entities. Ids are generated application-side
/// (GUID v7, time-ordered) in the DbContext SaveChanges pipeline.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Soft delete: rows stay in the database and are filtered out by a global query filter.
/// </summary>
public interface ISoftDeletable
{
    DateTime? DeletedAtUtc { get; set; }
}
