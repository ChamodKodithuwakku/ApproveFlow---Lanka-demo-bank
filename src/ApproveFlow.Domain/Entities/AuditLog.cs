using ApproveFlow.Domain.Enums;

namespace ApproveFlow.Domain.Entities;

/// <summary>
/// Append-only audit record. There is intentionally no method that mutates an existing instance; the
/// persistence layer refuses updates/deletes and a database trigger blocks them at the SQL level.
/// </summary>
public class AuditLog
{
    private AuditLog()
    {
    }

    public long Id { get; private set; }
    public AuditEventType EventType { get; private set; }

    /// <summary>Database table for entity changes; null for security and AI events.</summary>
    public string? TableName { get; private set; }

    public string? RecordKey { get; private set; }

    /// <summary>Insert / Update / Delete / SoftDelete for entity changes; event name (e.g. LoginFailed) otherwise.</summary>
    public string Action { get; private set; } = string.Empty;

    public string? OldValues { get; private set; }
    public string? NewValues { get; private set; }
    public string? ChangedColumns { get; private set; }
    public string? UserId { get; private set; }
    public string? UserName { get; private set; }
    public string? IpAddress { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTime TimestampUtc { get; private set; }

    public static AuditLog ForEntityChange(string tableName, string recordKey, string action, string? oldValues, string? newValues,
        string? changedColumns, AuditContext context) =>
        new()
        {
            EventType = AuditEventType.EntityChange,
            TableName = tableName,
            RecordKey = recordKey,
            Action = action,
            OldValues = oldValues,
            NewValues = newValues,
            ChangedColumns = changedColumns,
            UserId = context.UserId,
            UserName = context.UserName,
            IpAddress = context.IpAddress,
            CorrelationId = context.CorrelationId,
            TimestampUtc = context.TimestampUtc,
        };

    public static AuditLog ForEvent(AuditEventType eventType, string action, string? tableName, string? recordKey, string? details,
        AuditContext context) =>
        new()
        {
            EventType = eventType,
            TableName = tableName,
            RecordKey = recordKey,
            Action = action,
            NewValues = details,
            UserId = context.UserId,
            UserName = context.UserName,
            IpAddress = context.IpAddress,
            CorrelationId = context.CorrelationId,
            TimestampUtc = context.TimestampUtc,
        };
}

/// <summary>Who/where/when for an audit record.</summary>
public sealed record AuditContext(string? UserId, string? UserName, string? IpAddress, string? CorrelationId, DateTime TimestampUtc);
