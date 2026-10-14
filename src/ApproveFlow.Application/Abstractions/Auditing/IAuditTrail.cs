using ApproveFlow.Domain.Enums;

namespace ApproveFlow.Application.Abstractions.Auditing;

/// <summary>
/// Writes non-entity audit events (logins, lockouts, token issuance, AI requests). Entity inserts/updates/deletes
/// are captured automatically by the EF Core audit interceptor and do not go through this interface.
/// Events are persisted immediately and independently of any pending unit of work.
/// </summary>
public interface IAuditTrail
{
    /// <summary>Records an event attributed to the current user.</summary>
    Task RecordAsync(AuditEventType eventType, string action, string? tableName, string? recordKey, object? details,
        CancellationToken cancellationToken = default);

    /// <summary>Records a security event for an explicitly identified (possibly unauthenticated) user.</summary>
    Task RecordSecurityEventAsync(string action, string? userId, string? userName, object? details,
        CancellationToken cancellationToken = default);
}

/// <summary>Security event names written to AuditLog.Action.</summary>
public static class SecurityEvents
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string AccountLockedOut = "AccountLockedOut";
    public const string LoginBlockedInactive = "LoginBlockedInactive";
    public const string Logout = "Logout";
    public const string TokenIssued = "TokenIssued";
    public const string TokenDenied = "TokenDenied";
    public const string PasswordChanged = "PasswordChanged";
    public const string AccountUnlocked = "AccountUnlocked";
    public const string AccessDenied = "AccessDenied";
}
