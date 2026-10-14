using System.Globalization;
using System.Text;
using ApproveFlow.Application.Audit;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Web.Infrastructure;
using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApproveFlow.Web.Controllers;

/// <summary>Read-only audit trail viewer for Auditors, with filtering and CSV export.</summary>
[Authorize(Policy = Policies.AuditorOnly)]
public sealed class AuditLogsController : AppController
{
    private readonly IAuditLogService _auditLogs;

    public AuditLogsController(IAuditLogService auditLogs)
    {
        _auditLogs = auditLogs;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AuditLogFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var (tables, actions) = await _auditLogs.GetFilterOptionsAsync(cancellationToken);
        var result = await _auditLogs.SearchAsync(filter.ToQuery(), cancellationToken);
        return View(new AuditLogIndexViewModel(filter, result, tables, actions));
    }

    /// <summary>Streams the filtered audit trail as CSV (UTF-8 with BOM for Excel). The export itself is audited.</summary>
    [HttpGet]
    public async Task Export([FromQuery] AuditLogFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var rows = await _auditLogs.ExportAsync(filter.ToQuery() with { Page = 1 }, cancellationToken);

        var fileName = string.Create(CultureInfo.InvariantCulture, $"approveflow-audit-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

        await using var writer = new StreamWriter(Response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 16 * 1024,
            leaveOpen: true);
        await writer.WriteLineAsync(Csv.Row("Id", "TimestampUtc", "TimestampColombo", "EventType", "Table", "RecordKey", "Action",
            "ChangedColumns", "OldValues", "NewValues", "UserId", "UserName", "IpAddress", "CorrelationId"));

        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            await writer.WriteLineAsync(Csv.Row(
                row.Id.ToString(CultureInfo.InvariantCulture),
                row.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                Display.Time(row.TimestampUtc),
                row.EventType.ToString(),
                row.TableName,
                row.RecordKey,
                row.Action,
                row.ChangedColumns,
                row.OldValues,
                row.NewValues,
                row.UserId,
                row.UserName,
                row.IpAddress,
                row.CorrelationId));
        }

        await writer.FlushAsync(cancellationToken);
    }
}

public sealed class AuditLogFilter
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? User { get; set; }
    public string? Table { get; set; }
    public string? Action { get; set; }
    public AuditEventType? EventType { get; set; }
    public string? RecordKey { get; set; }
    public int Page { get; set; } = 1;

    public AuditLogQuery ToQuery() => new()
    {
        FromDate = From,
        ToDate = To,
        User = User,
        TableName = Table,
        Action = Action,
        EventType = EventType,
        RecordKey = RecordKey,
        Page = Page,
        PageSize = 50,
    };

    public Dictionary<string, string?> RouteValues(int? page = null) => new()
    {
        ["From"] = From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["To"] = To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["User"] = User,
        ["Table"] = Table,
        ["Action"] = Action,
        ["EventType"] = EventType?.ToString(),
        ["RecordKey"] = RecordKey,
        ["Page"] = (page ?? Page).ToString(CultureInfo.InvariantCulture),
    };
}

public sealed record AuditLogIndexViewModel(
    AuditLogFilter Filter,
    Application.Common.Models.PagedResult<AuditLogDto> Result,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Actions);
