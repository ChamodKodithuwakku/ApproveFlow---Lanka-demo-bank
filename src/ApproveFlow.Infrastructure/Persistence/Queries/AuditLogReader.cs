using ApproveFlow.Application.Audit;
using ApproveFlow.Application.Common.Models;
using ApproveFlow.Application.Common.Time;
using ApproveFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApproveFlow.Infrastructure.Persistence.Queries;

/// <summary>Read-only queries over AuditLogs. The type intentionally exposes no way to change audit data.</summary>
internal sealed class AuditLogReader : IAuditLogReader
{
    private readonly ApplicationDbContext _db;

    public AuditLogReader(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, pageSize) = Paging.Normalise(query.Page, query.PageSize);
        var source = Filter(query);
        var total = await source.CountAsync(cancellationToken);
        var items = await Project(source.OrderByDescending(a => a.Id).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new PagedResult<AuditLogDto>(items, page, pageSize, total);
    }

    public IAsyncEnumerable<AuditLogDto> StreamAsync(AuditLogQuery query, int maxRows, CancellationToken cancellationToken = default) =>
        Project(Filter(query).OrderByDescending(a => a.Id).Take(maxRows)).AsAsyncEnumerable();

    public async Task<IReadOnlyList<string>> GetTableNamesAsync(CancellationToken cancellationToken = default) =>
        await _db.AuditLogs.AsNoTracking().Where(a => a.TableName != null).Select(a => a.TableName!).Distinct().OrderBy(t => t)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> GetActionsAsync(CancellationToken cancellationToken = default) =>
        await _db.AuditLogs.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(cancellationToken);

    private IQueryable<AuditLog> Filter(AuditLogQuery query)
    {
        var source = _db.AuditLogs.AsNoTracking();
        var (fromUtc, toUtcExclusive) = ColomboTime.ToUtcRange(query.FromDate, query.ToDate);
        if (fromUtc.HasValue)
        {
            source = source.Where(a => a.TimestampUtc >= fromUtc.Value);
        }

        if (toUtcExclusive.HasValue)
        {
            source = source.Where(a => a.TimestampUtc < toUtcExclusive.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.User))
        {
            var user = query.User.Trim();
            source = source.Where(a => (a.UserName != null && a.UserName.Contains(user)) || a.UserId == user);
        }

        if (!string.IsNullOrWhiteSpace(query.TableName))
        {
            source = source.Where(a => a.TableName == query.TableName);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            source = source.Where(a => a.Action == query.Action);
        }

        if (query.EventType.HasValue)
        {
            source = source.Where(a => a.EventType == query.EventType.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.RecordKey))
        {
            source = source.Where(a => a.RecordKey == query.RecordKey);
        }

        return source;
    }

    private static IQueryable<AuditLogDto> Project(IQueryable<AuditLog> source) =>
        source.Select(a => new AuditLogDto(a.Id, a.EventType, a.TableName, a.RecordKey, a.Action, a.OldValues, a.NewValues, a.ChangedColumns,
            a.UserId, a.UserName, a.IpAddress, a.CorrelationId, a.TimestampUtc));
}
