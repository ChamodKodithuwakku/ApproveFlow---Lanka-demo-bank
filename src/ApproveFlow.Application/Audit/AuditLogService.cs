using ApproveFlow.Application.Abstractions.Auditing;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Application.Common.Models;
using ApproveFlow.Application.Common.Security;
using ApproveFlow.Domain.Enums;

namespace ApproveFlow.Application.Audit;

public sealed record AuditLogQuery
{
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }

    /// <summary>Matches user name (email) or user id.</summary>
    public string? User { get; init; }

    public string? TableName { get; init; }
    public string? Action { get; init; }
    public AuditEventType? EventType { get; init; }
    public string? RecordKey { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed record AuditLogDto(
    long Id,
    AuditEventType EventType,
    string? TableName,
    string? RecordKey,
    string Action,
    string? OldValues,
    string? NewValues,
    string? ChangedColumns,
    string? UserId,
    string? UserName,
    string? IpAddress,
    string? CorrelationId,
    DateTime TimestampUtc);

/// <summary>Read-only access to the audit trail (implemented in Infrastructure). There is deliberately no write method.</summary>
public interface IAuditLogReader
{
    Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AuditLogDto> StreamAsync(AuditLogQuery query, int maxRows, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetTableNamesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetActionsAsync(CancellationToken cancellationToken = default);
}

public interface IAuditLogService
{
    Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<string> Tables, IReadOnlyList<string> Actions)> GetFilterOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Streams matching rows for CSV export (capped) and records the export itself in the audit trail.</summary>
    Task<IAsyncEnumerable<AuditLogDto>> ExportAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
}

internal sealed class AuditLogService : IAuditLogService
{
    public const int MaxExportRows = 50_000;

    private readonly IAuditLogReader _reader;
    private readonly IAuditTrail _auditTrail;
    private readonly ICurrentUser _currentUser;

    public AuditLogService(IAuditLogReader reader, IAuditTrail auditTrail, ICurrentUser currentUser)
    {
        _reader = reader;
        _auditTrail = auditTrail;
        _currentUser = currentUser;
    }

    public Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(AppRoles.Auditor);
        return _reader.SearchAsync(query, cancellationToken);
    }

    public async Task<(IReadOnlyList<string> Tables, IReadOnlyList<string> Actions)> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(AppRoles.Auditor);
        return (await _reader.GetTableNamesAsync(cancellationToken), await _reader.GetActionsAsync(cancellationToken));
    }

    public async Task<IAsyncEnumerable<AuditLogDto>> ExportAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _currentUser.RequireRole(AppRoles.Auditor);
        await _auditTrail.RecordAsync(AuditEventType.Security, "AuditLogExported", "AuditLogs", null,
            new { query.FromDate, query.ToDate, query.User, query.TableName, query.Action, query.EventType, query.RecordKey, MaxRows = MaxExportRows },
            cancellationToken);
        return _reader.StreamAsync(query, MaxExportRows, cancellationToken);
    }
}
