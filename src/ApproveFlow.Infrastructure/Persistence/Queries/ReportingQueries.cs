using System.Data;
using System.Data.Common;
using ApproveFlow.Application.Common.Time;
using ApproveFlow.Application.Reports;
using ApproveFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ApproveFlow.Infrastructure.Persistence.Queries;

/// <summary>
/// Reporting read model. On SQL Server it executes the stored procedures in /database through parameterised ADO.NET
/// (CommandType.StoredProcedure, typed parameters, multiple result sets) - no SQL is ever built from user input.
/// On SQLite (tests / demo mode) an equivalent LINQ implementation produces the same shapes.
/// </summary>
internal sealed class ReportingQueries : IReportingQueries
{
    public const string ApprovalSummaryProcedure = "dbo.usp_GetApprovalSummary";
    public const string CheckerPerformanceProcedure = "dbo.usp_GetCheckerPerformance";

    private static readonly string[] StatusOrder =
        { "Draft", "Submitted", "PendingSecondApproval", "ReturnedForCorrection", "Approved", "Rejected" };

    private readonly ApplicationDbContext _db;

    public ReportingQueries(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<ApprovalSummaryReport> GetApprovalSummaryAsync(ReportCriteria criteria, CancellationToken cancellationToken = default) =>
        _db.IsSqlServer ? ApprovalSummaryFromProcedureAsync(criteria, cancellationToken) : ApprovalSummaryPortableAsync(criteria, cancellationToken);

    public Task<IReadOnlyList<CheckerPerformanceRow>> GetCheckerPerformanceAsync(DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken = default) =>
        _db.IsSqlServer
            ? CheckerPerformanceFromProcedureAsync(fromDate, toDate, cancellationToken)
            : CheckerPerformancePortableAsync(fromDate, toDate, cancellationToken);

    // ---------------------------------------------------------------- SQL Server (stored procedures)

    private async Task<ApprovalSummaryReport> ApprovalSummaryFromProcedureAsync(ReportCriteria criteria, CancellationToken cancellationToken)
    {
        return await WithCommandAsync(ApprovalSummaryProcedure, command =>
        {
            AddParameter(command, "@FromDate", DbType.Date, criteria.FromDate.ToDateTime(TimeOnly.MinValue));
            AddParameter(command, "@ToDate", DbType.Date, criteria.ToDate.ToDateTime(TimeOnly.MinValue));
            AddParameter(command, "@BranchId", DbType.Int32, criteria.BranchId.HasValue ? criteria.BranchId.Value : DBNull.Value);
        }, async reader =>
        {
            var byStatus = new List<StatusSummaryRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                byStatus.Add(new StatusSummaryRow(reader.GetString("Status"), reader.GetInt32("ApplicationCount"), reader.GetDecimal("TotalAmount")));
            }

            await reader.NextResultAsync(cancellationToken);
            var byBranch = new List<BranchSummaryRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                byBranch.Add(new BranchSummaryRow(reader.GetInt32("BranchId"), reader.GetString("BranchCode"), reader.GetString("BranchName"),
                    reader.GetInt32("ApplicationCount"), reader.GetDecimal("TotalAmount"), reader.GetInt32("ApprovedCount"),
                    reader.GetDecimal("ApprovedAmount"), reader.GetInt32("RejectedCount"), reader.GetInt32("PendingCount")));
            }

            await reader.NextResultAsync(cancellationToken);
            var byProduct = new List<ProductSummaryRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                byProduct.Add(new ProductSummaryRow(reader.GetInt32("ProductId"), reader.GetString("ProductCode"), reader.GetString("ProductName"),
                    reader.GetInt32("ApplicationCount"), reader.GetDecimal("TotalAmount"), reader.GetInt32("ApprovedCount"),
                    reader.GetDecimal("ApprovedAmount"), reader.GetDecimal("AverageAmount")));
            }

            await reader.NextResultAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var overall = new OverallSummary(reader.GetInt32("TotalApplications"), reader.GetDecimal("TotalAmount"), reader.GetInt32("ApprovedCount"),
                reader.GetInt32("RejectedCount"), reader.GetInt32("DecidedCount"), NullableDecimal(reader, "AverageTurnaroundHours"),
                NullableDecimal(reader, "MinTurnaroundHours"), NullableDecimal(reader, "MaxTurnaroundHours"));

            return new ApprovalSummaryReport(criteria, overall, byStatus, byBranch, byProduct, ApprovalSummaryProcedure);
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<CheckerPerformanceRow>> CheckerPerformanceFromProcedureAsync(DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken)
    {
        return await WithCommandAsync(CheckerPerformanceProcedure, command =>
        {
            AddParameter(command, "@FromDate", DbType.Date, fromDate.ToDateTime(TimeOnly.MinValue));
            AddParameter(command, "@ToDate", DbType.Date, toDate.ToDateTime(TimeOnly.MinValue));
        }, async reader =>
        {
            var rows = new List<CheckerPerformanceRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new CheckerPerformanceRow(reader.GetString("CheckerId"), reader.GetString("CheckerName"), reader.GetInt32("TotalDecisions"),
                    reader.GetInt32("ApprovedCount"), reader.GetInt32("FirstApprovalCount"), reader.GetInt32("RejectedCount"),
                    reader.GetInt32("ReturnedCount"), reader.GetDecimal("ApprovedAmount"), NullableDecimal(reader, "AverageResponseHours")));
            }

            return (IReadOnlyList<CheckerPerformanceRow>)rows;
        }, cancellationToken);
    }

    private async Task<T> WithCommandAsync<T>(string procedure, Action<DbCommand> configure, Func<DbDataReader, Task<T>> read,
        CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = procedure;
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = 60;
            command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            configure(command);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await read(reader);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static decimal? NullableDecimal(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }

    // ---------------------------------------------------------------- Portable equivalents (SQLite)

    private async Task<ApprovalSummaryReport> ApprovalSummaryPortableAsync(ReportCriteria criteria, CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = ColomboTime.ToUtcRange(criteria.FromDate, criteria.ToDate);
        var scope = await _db.LoanApplications.AsNoTracking()
            .Where(a => a.CreatedAtUtc >= fromUtc!.Value && a.CreatedAtUtc < toUtc!.Value)
            .Where(a => criteria.BranchId == null || a.BranchId == criteria.BranchId)
            .Select(a => new
            {
                a.Id, a.Status, a.Amount, a.BranchId, BranchCode = a.Branch!.Code, BranchName = a.Branch.Name, a.LoanProductId,
                ProductCode = a.LoanProduct!.Code, ProductName = a.LoanProduct.Name, a.DecidedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var ids = scope.Select(s => s.Id).ToList();
        var firstSubmitted = (await _db.ApprovalActions.AsNoTracking()
                .Where(x => ids.Contains(x.LoanApplicationId) && x.ActionType == ApprovalActionType.Submitted)
                .Select(x => new { x.LoanApplicationId, x.PerformedAtUtc })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.LoanApplicationId)
            .ToDictionary(g => g.Key, g => g.Min(x => x.PerformedAtUtc));

        var byStatus = scope.GroupBy(s => s.Status.ToString())
            .OrderBy(g => Array.IndexOf(StatusOrder, g.Key))
            .Select(g => new StatusSummaryRow(g.Key, g.Count(), g.Sum(x => x.Amount)))
            .ToList();

        var byBranch = scope.GroupBy(s => new { s.BranchId, s.BranchCode, s.BranchName })
            .OrderBy(g => g.Key.BranchName)
            .Select(g => new BranchSummaryRow(g.Key.BranchId, g.Key.BranchCode, g.Key.BranchName, g.Count(), g.Sum(x => x.Amount),
                g.Count(x => x.Status == LoanStatus.Approved), g.Where(x => x.Status == LoanStatus.Approved).Sum(x => x.Amount),
                g.Count(x => x.Status == LoanStatus.Rejected),
                g.Count(x => x.Status is LoanStatus.Submitted or LoanStatus.PendingSecondApproval)))
            .ToList();

        var byProduct = scope.GroupBy(s => new { s.LoanProductId, s.ProductCode, s.ProductName })
            .OrderBy(g => g.Key.ProductName)
            .Select(g => new ProductSummaryRow(g.Key.LoanProductId, g.Key.ProductCode, g.Key.ProductName, g.Count(), g.Sum(x => x.Amount),
                g.Count(x => x.Status == LoanStatus.Approved), g.Where(x => x.Status == LoanStatus.Approved).Sum(x => x.Amount),
                Math.Round(g.Average(x => x.Amount), 2)))
            .ToList();

        var turnaround = scope
            .Where(s => s.Status is LoanStatus.Approved or LoanStatus.Rejected && s.DecidedAtUtc.HasValue && firstSubmitted.ContainsKey(s.Id))
            .Select(s => (decimal)Math.Round((s.DecidedAtUtc!.Value - firstSubmitted[s.Id]).TotalMinutes / 60.0, 2))
            .ToList();

        var overall = new OverallSummary(scope.Count, scope.Sum(s => s.Amount), scope.Count(s => s.Status == LoanStatus.Approved),
            scope.Count(s => s.Status == LoanStatus.Rejected), turnaround.Count,
            turnaround.Count == 0 ? null : Math.Round(turnaround.Average(), 2),
            turnaround.Count == 0 ? null : turnaround.Min(),
            turnaround.Count == 0 ? null : turnaround.Max());

        return new ApprovalSummaryReport(criteria, overall, byStatus, byBranch, byProduct, "Portable LINQ query (SQLite demo mode)");
    }

    private async Task<IReadOnlyList<CheckerPerformanceRow>> CheckerPerformancePortableAsync(DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = ColomboTime.ToUtcRange(fromDate, toDate);
        var decisionTypes = new[]
        {
            ApprovalActionType.FirstApproval, ApprovalActionType.Approved, ApprovalActionType.Rejected, ApprovalActionType.ReturnedForCorrection,
        };

        var decisions = await _db.ApprovalActions.AsNoTracking()
            .Where(a => decisionTypes.Contains(a.ActionType) && a.PerformedAtUtc >= fromUtc!.Value && a.PerformedAtUtc < toUtc!.Value)
            .Select(a => new { a.LoanApplicationId, a.PerformedById, a.PerformedByName, a.ActionType, a.PerformedAtUtc, a.LoanApplication!.Amount })
            .ToListAsync(cancellationToken);

        var ids = decisions.Select(d => d.LoanApplicationId).Distinct().ToList();
        var submissions = (await _db.ApprovalActions.AsNoTracking()
                .Where(a => ids.Contains(a.LoanApplicationId)
                            && (a.ActionType == ApprovalActionType.Submitted || a.ActionType == ApprovalActionType.Resubmitted))
                .Select(a => new { a.LoanApplicationId, a.PerformedAtUtc })
                .ToListAsync(cancellationToken))
            .ToLookup(a => a.LoanApplicationId, a => a.PerformedAtUtc);

        // Response time = decision time minus the latest (re)submission before it, as in the stored procedure.
        double? ResponseHours(int applicationId, DateTime decidedAtUtc)
        {
            var submittedAt = submissions[applicationId].Where(s => s <= decidedAtUtc).OrderByDescending(s => s).FirstOrDefault();
            return submittedAt == default ? null : (decidedAtUtc - submittedAt).TotalMinutes / 60.0;
        }

        return decisions
            .GroupBy(d => d.PerformedById)
            .Select(g =>
            {
                var responseHours = g.Select(d => ResponseHours(d.LoanApplicationId, d.PerformedAtUtc)).OfType<double>().ToList();

                return new CheckerPerformanceRow(g.Key, g.Max(d => d.PerformedByName)!, g.Count(),
                    g.Count(d => d.ActionType == ApprovalActionType.Approved), g.Count(d => d.ActionType == ApprovalActionType.FirstApproval),
                    g.Count(d => d.ActionType == ApprovalActionType.Rejected), g.Count(d => d.ActionType == ApprovalActionType.ReturnedForCorrection),
                    g.Where(d => d.ActionType == ApprovalActionType.Approved).Sum(d => d.Amount),
                    responseHours.Count == 0 ? null : Math.Round((decimal)responseHours.Average(), 2));
            })
            .OrderByDescending(r => r.TotalDecisions).ThenBy(r => r.CheckerName)
            .ToList();
    }
}
