using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Application.Common.Security;
using ApproveFlow.Application.Common.Time;
using ValidationException = ApproveFlow.Application.Common.Exceptions.ValidationException;

namespace ApproveFlow.Application.Reports;

/// <summary>Report period (inclusive Asia/Colombo dates) and optional branch filter.</summary>
public sealed record ReportCriteria(DateOnly FromDate, DateOnly ToDate, int? BranchId);

/// <summary>Applications and amounts per status.</summary>
public sealed record StatusSummaryRow(string Status, int ApplicationCount, decimal TotalAmount);

/// <summary>Volumes and outcomes per branch.</summary>
public sealed record BranchSummaryRow(int BranchId, string BranchCode, string BranchName, int ApplicationCount, decimal TotalAmount,
    int ApprovedCount, decimal ApprovedAmount, int RejectedCount, int PendingCount);

/// <summary>Volumes and outcomes per loan product.</summary>
public sealed record ProductSummaryRow(int ProductId, string ProductCode, string ProductName, int ApplicationCount, decimal TotalAmount,
    int ApprovedCount, decimal ApprovedAmount, decimal AverageAmount);

/// <summary>Totals and turnaround (first submission to final Approved/Rejected decision) for the period.</summary>
public sealed record OverallSummary(int TotalApplications, decimal TotalAmount, int ApprovedCount, int RejectedCount, int DecidedCount,
    decimal? AverageTurnaroundHours, decimal? MinTurnaroundHours, decimal? MaxTurnaroundHours)
{
    /// <summary>Approved as a percentage of final decisions (approved + rejected).</summary>
    public decimal? ApprovalRatePercent =>
        ApprovedCount + RejectedCount == 0 ? null : Math.Round(ApprovedCount * 100m / (ApprovedCount + RejectedCount), 1);
}

/// <summary>Result of dbo.usp_GetApprovalSummary.</summary>
/// <param name="Source">Where the figures came from (stored procedure on SQL Server, portable query on SQLite).</param>
public sealed record ApprovalSummaryReport(ReportCriteria Criteria, OverallSummary Overall, IReadOnlyList<StatusSummaryRow> ByStatus,
    IReadOnlyList<BranchSummaryRow> ByBranch, IReadOnlyList<ProductSummaryRow> ByProduct, string Source);

/// <summary>Result row of dbo.usp_GetCheckerPerformance.</summary>
public sealed record CheckerPerformanceRow(string CheckerId, string CheckerName, int TotalDecisions, int ApprovedCount, int FirstApprovalCount,
    int RejectedCount, int ReturnedCount, decimal ApprovedAmount, decimal? AverageResponseHours);

/// <summary>Reporting read model (Infrastructure calls the SQL Server stored procedures with parameterised ADO.NET).</summary>
public interface IReportingQueries
{
    Task<ApprovalSummaryReport> GetApprovalSummaryAsync(ReportCriteria criteria, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CheckerPerformanceRow>> GetCheckerPerformanceAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}

public interface IReportService
{
    Task<ApprovalSummaryReport> GetApprovalSummaryAsync(ReportCriteria criteria, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CheckerPerformanceRow>> GetCheckerPerformanceAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    ReportCriteria DefaultCriteria();
}

internal sealed class ReportService : IReportService
{
    public const int MaxRangeDays = 731;

    private readonly IReportingQueries _queries;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public ReportService(IReportingQueries queries, ICurrentUser currentUser, TimeProvider clock)
    {
        _queries = queries;
        _currentUser = currentUser;
        _clock = clock;
    }

    public ReportCriteria DefaultCriteria()
    {
        var today = ColomboTime.Today(_clock);
        return new ReportCriteria(today.AddDays(-89), today, null);
    }

    public Task<ApprovalSummaryReport> GetApprovalSummaryAsync(ReportCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        _currentUser.RequireRole(AppRoles.Admin, AppRoles.Checker, AppRoles.Auditor);
        Validate(criteria.FromDate, criteria.ToDate);
        return _queries.GetApprovalSummaryAsync(criteria, cancellationToken);
    }

    public Task<IReadOnlyList<CheckerPerformanceRow>> GetCheckerPerformanceAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(AppRoles.Admin, AppRoles.Checker, AppRoles.Auditor);
        Validate(fromDate, toDate);
        return _queries.GetCheckerPerformanceAsync(fromDate, toDate, cancellationToken);
    }

    private static void Validate(DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate)
        {
            throw new ValidationException("ToDate", "The end date cannot be earlier than the start date.");
        }

        if (toDate.DayNumber - fromDate.DayNumber > MaxRangeDays)
        {
            throw new ValidationException("ToDate", $"The report period cannot exceed {MaxRangeDays} days.");
        }
    }
}
