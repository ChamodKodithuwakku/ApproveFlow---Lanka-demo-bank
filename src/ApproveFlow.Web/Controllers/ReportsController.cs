using ApproveFlow.Application.LoanApplications;
using ApproveFlow.Application.Lookups;
using ApproveFlow.Application.Reports;
using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ValidationException = ApproveFlow.Application.Common.Exceptions.ValidationException;

namespace ApproveFlow.Web.Controllers;

/// <summary>Management reports backed by dbo.usp_GetApprovalSummary and dbo.usp_GetCheckerPerformance.</summary>
[Authorize(Policy = Policies.ReportViewer)]
public sealed class ReportsController : AppController
{
    private readonly IReportService _reports;
    private readonly ILookupService _lookups;

    public ReportsController(IReportService reports, ILookupService lookups)
    {
        _reports = reports;
        _lookups = lookups;
    }

    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, int? branchId, CancellationToken cancellationToken)
    {
        var defaults = _reports.DefaultCriteria();
        var criteria = new ReportCriteria(from ?? defaults.FromDate, to ?? defaults.ToDate, branchId);
        var branches = await _lookups.GetBranchesAsync(cancellationToken);

        try
        {
            var summary = await _reports.GetApprovalSummaryAsync(criteria, cancellationToken);
            var checkers = await _reports.GetCheckerPerformanceAsync(criteria.FromDate, criteria.ToDate, cancellationToken);
            return View(new ReportsViewModel(criteria, branches, summary, checkers, null));
        }
        catch (ValidationException ex)
        {
            return View(new ReportsViewModel(criteria, branches, null, Array.Empty<CheckerPerformanceRow>(),
                string.Join(" ", ex.Errors.SelectMany(e => e.Value))));
        }
    }
}

public sealed record ReportsViewModel(
    ReportCriteria Criteria,
    IReadOnlyList<LookupItem> Branches,
    ApprovalSummaryReport? Summary,
    IReadOnlyList<CheckerPerformanceRow> Checkers,
    string? Error);
