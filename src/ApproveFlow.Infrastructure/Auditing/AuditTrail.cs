using System.Text.Json;
using ApproveFlow.Application.Abstractions.Auditing;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Domain.Entities;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApproveFlow.Infrastructure.Auditing;

/// <summary>
/// Persists security and AI audit events through a dedicated, short-lived DbContext so that recording an event
/// never flushes (or is rolled back with) unrelated pending changes in the request's unit of work.
/// </summary>
internal sealed class AuditTrail : IAuditTrail
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public AuditTrail(DbContextOptions<ApplicationDbContext> options, ICurrentUser currentUser, TimeProvider clock)
    {
        _options = options;
        _currentUser = currentUser;
        _clock = clock;
    }

    public Task RecordAsync(AuditEventType eventType, string action, string? tableName, string? recordKey, object? details,
        CancellationToken cancellationToken = default)
    {
        var context = new AuditContext(_currentUser.UserId, _currentUser.UserName, _currentUser.IpAddress, _currentUser.CorrelationId,
            _clock.GetUtcNow().UtcDateTime);
        return WriteAsync(AuditLog.ForEvent(eventType, action, tableName, recordKey, Serialize(details), context), cancellationToken);
    }

    public Task RecordSecurityEventAsync(string action, string? userId, string? userName, object? details,
        CancellationToken cancellationToken = default)
    {
        var context = new AuditContext(userId, userName, _currentUser.IpAddress, _currentUser.CorrelationId, _clock.GetUtcNow().UtcDateTime);
        return WriteAsync(AuditLog.ForEvent(AuditEventType.Security, action, "AspNetUsers", userId, Serialize(details), context), cancellationToken);
    }

    private async Task WriteAsync(AuditLog log, CancellationToken cancellationToken)
    {
        await using var db = new ApplicationDbContext(_options);
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Serialize(object? details) => details switch
    {
        null => null,
        string s => s,
        _ => JsonSerializer.Serialize(details),
    };
}
