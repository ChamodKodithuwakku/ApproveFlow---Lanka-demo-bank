namespace ApproveFlow.Domain.Common;

/// <summary>Base type for entities with an integer surrogate key.</summary>
public abstract class Entity
{
    public int Id { get; protected set; }
}

/// <summary>
/// Entity that carries created/updated stamps. Every <see cref="AuditableEntity"/> is also
/// captured by the audit-trail interceptor (old/new values written to AuditLog).
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAtUtc { get; private set; }
    public string CreatedById { get; private set; } = string.Empty;
    public string CreatedByName { get; private set; } = string.Empty;
    public DateTime? UpdatedAtUtc { get; private set; }
    public string? UpdatedById { get; private set; }
    public string? UpdatedByName { get; private set; }

    /// <summary>Sets the creation stamp once; later calls are ignored so an explicit (e.g. back-dated seed) value wins.</summary>
    public void StampCreated(string userId, string userName, DateTime nowUtc)
    {
        if (CreatedAtUtc != default && !string.IsNullOrEmpty(CreatedById))
        {
            return;
        }

        CreatedAtUtc = CreatedAtUtc == default ? nowUtc : CreatedAtUtc;
        CreatedById = string.IsNullOrEmpty(CreatedById) ? userId : CreatedById;
        CreatedByName = string.IsNullOrEmpty(CreatedByName) ? userName : CreatedByName;
    }

    public void StampUpdated(string userId, string userName, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedById = userId;
        UpdatedByName = userName;
    }
}

/// <summary>Records are never physically deleted; a global query filter hides soft-deleted rows.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
}
