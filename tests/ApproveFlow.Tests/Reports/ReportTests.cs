using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Application.Reports;
using ApproveFlow.Infrastructure.Persistence;
using ApproveFlow.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace ApproveFlow.Tests.Reports;

public sealed class ReportTests : IAsyncLifetime
{
    private SqliteTestHost _host = null!;

    public async Task InitializeAsync() => _host = await SqliteTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Approval_summary_totals_are_consistent_across_breakdowns()
    {
        await _host.SignInAsync("auditor@demo.lk");
        var report = await _host.WithScopeAsync(sp =>
        {
            var reports = sp.GetRequiredService<IReportService>();
            return reports.GetApprovalSummaryAsync(reports.DefaultCriteria());
        });

        Assert.Equal(60, report.Overall.TotalApplications);
        Assert.Equal(report.Overall.TotalApplications, report.ByStatus.Sum(s => s.ApplicationCount));
        Assert.Equal(report.Overall.TotalApplications, report.ByBranch.Sum(b => b.ApplicationCount));
        Assert.Equal(report.Overall.TotalAmount, report.ByProduct.Sum(p => p.TotalAmount));
        Assert.Equal(3, report.ByBranch.Count);
        Assert.True(report.Overall.DecidedCount > 0);
        Assert.NotNull(report.Overall.AverageTurnaroundHours);
    }

    [Fact]
    public async Task Checker_performance_lists_both_checkers()
    {
        await _host.SignInAsync("checker1@demo.lk");
        var rows = await _host.WithScopeAsync(sp =>
        {
            var reports = sp.GetRequiredService<IReportService>();
            var criteria = reports.DefaultCriteria();
            return reports.GetCheckerPerformanceAsync(criteria.FromDate, criteria.ToDate);
        });

        Assert.Contains(rows, r => r.CheckerName == "Ruwan Senanayake" && r.FirstApprovalCount > 0);
        Assert.Contains(rows, r => r.CheckerName == "Shanika Gunasekara" && r.ApprovedCount > 0);
        Assert.All(rows, r => Assert.Equal(r.TotalDecisions, r.ApprovedCount + r.FirstApprovalCount + r.RejectedCount + r.ReturnedCount));
    }

    [Fact]
    public async Task Makers_cannot_run_reports_and_invalid_periods_are_rejected()
    {
        await _host.SignInAsync("maker1@demo.lk");
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => _host.WithScopeAsync(sp =>
            sp.GetRequiredService<IReportService>().GetApprovalSummaryAsync(new ReportCriteria(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), null))));

        await _host.SignInAsync("auditor@demo.lk");
        await Assert.ThrowsAsync<ValidationException>(() => _host.WithScopeAsync(sp =>
            sp.GetRequiredService<IReportService>().GetApprovalSummaryAsync(new ReportCriteria(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), null))));
    }
}

/// <summary>
/// The stored procedures, view and trigger cannot run on SQLite, so CI verifies them statically: every script in
/// /database must parse as valid T-SQL (SQL Server 2022 grammar) and be embedded for the migration.
/// </summary>
public sealed class SqlScriptTests
{
    public static TheoryData<string> Scripts()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "database"), "*.sql", SearchOption.AllDirectories))
        {
            data.Add(Path.GetRelativePath(AppContext.BaseDirectory, file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void Database_scripts_are_valid_tsql(string relativePath)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, relativePath)));
        parser.Parse(reader, out var errors);

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(e => $"{relativePath} line {e.Line}: {e.Message}")));
    }

    [Theory]
    [InlineData(SqlScripts.ApprovalSummaryProcedure, "CREATE OR ALTER PROCEDURE dbo.usp_GetApprovalSummary")]
    [InlineData(SqlScripts.CheckerPerformanceProcedure, "CREATE OR ALTER PROCEDURE dbo.usp_GetCheckerPerformance")]
    [InlineData(SqlScripts.PendingAgingView, "CREATE OR ALTER VIEW dbo.vw_PendingApprovalsAging")]
    [InlineData(SqlScripts.AuditAppendOnlyTrigger, "CREATE OR ALTER TRIGGER dbo.trg_AuditLogs_AppendOnly")]
    public void Migration_embeds_each_database_object(string script, string expected)
    {
        Assert.Contains(expected, SqlScripts.Read(script), StringComparison.Ordinal);
        Assert.StartsWith("EXEC(N'", SqlScripts.ReadAsDynamicSql(script), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sqlite_schema_has_the_expected_check_constraints()
    {
        // Check constraints are enforced by the database itself, not only by the application.
        await using var host = await SqliteTestHost.CreateAsync(seedDemoData: false);
        var sql = await host.QueryAsync(db => db.Database.SqlQuery<string>($"SELECT sql AS Value FROM sqlite_master WHERE name = 'LoanApplications'").SingleAsync());
        Assert.Contains("CK_LoanApplications_Amount", sql, StringComparison.Ordinal);
        Assert.Contains("CK_LoanApplications_Tenure", sql, StringComparison.Ordinal);
    }
}
