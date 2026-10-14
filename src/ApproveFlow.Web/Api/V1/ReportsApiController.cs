using System.ComponentModel.DataAnnotations;
using ApproveFlow.Application.Reports;
using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApproveFlow.Web.Api.V1;

/// <summary>Management reporting.</summary>
[Route("api/v1/reports")]
[Tags("Reports")]
[Authorize(Policy = Policies.ReportViewer)]
public sealed class ReportsApiController : ApiControllerBase
{
    private readonly IReportService _reports;

    public ReportsApiController(IReportService reports)
    {
        _reports = reports;
    }

    /// <summary>Approval summary by status, branch and product with turnaround times (Admin, Checker, Auditor).</summary>
    /// <remarks>
    /// Executes the SQL Server stored procedure <c>dbo.usp_GetApprovalSummary(@FromDate, @ToDate, @BranchId)</c>.
    /// Dates are inclusive Asia/Colombo business dates (yyyy-MM-dd); when omitted the last 90 days are used.
    /// </remarks>
    /// <response code="200">The summary.</response>
    /// <response code="403">Caller is a Maker (reports are for oversight roles).</response>
    /// <response code="422">Invalid period (end before start, or longer than two years).</response>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApprovalSummaryReport), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<ApprovalSummaryReport> Summary([FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate,
        [FromQuery, Range(1, int.MaxValue)] int? branchId, CancellationToken cancellationToken)
    {
        var defaults = _reports.DefaultCriteria();
        return _reports.GetApprovalSummaryAsync(new ReportCriteria(fromDate ?? defaults.FromDate, toDate ?? defaults.ToDate, branchId),
            cancellationToken);
    }

    /// <summary>Decision counts, approved amount and average response time per checker.</summary>
    /// <remarks>Executes <c>dbo.usp_GetCheckerPerformance(@FromDate, @ToDate)</c>.</remarks>
    /// <response code="200">One row per checker.</response>
    [HttpGet("checker-performance")]
    [ProducesResponseType(typeof(IReadOnlyList<CheckerPerformanceRow>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<IReadOnlyList<CheckerPerformanceRow>> CheckerPerformance([FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate,
        CancellationToken cancellationToken)
    {
        var defaults = _reports.DefaultCriteria();
        return _reports.GetCheckerPerformanceAsync(fromDate ?? defaults.FromDate, toDate ?? defaults.ToDate, cancellationToken);
    }
}
