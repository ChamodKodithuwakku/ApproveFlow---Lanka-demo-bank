using System.Text;
using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Application.DataImport;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApproveFlow.Tests.DataImport;

public sealed class LegacyImportTests : IAsyncLifetime
{
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample-legacy-loans.csv");
    private SqliteTestHost _host = null!;

    public async Task InitializeAsync() => _host = await SqliteTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<ImportResultDto> ImportAsync(Stream content, string fileName = "sample-legacy-loans.csv") =>
        _host.WithScopeAsync(sp => sp.GetRequiredService<ILegacyLoanImportService>().ImportAsync(content, fileName));

    [Fact]
    public async Task Csv_import_rejects_invalid_rows_and_reconciles_totals()
    {
        await _host.SignInAsync("admin@demo.lk");
        var before = await _host.QueryAsync(db => db.LoanApplications.CountAsync());

        await using var file = File.OpenRead(SamplePath);
        var result = await ImportAsync(file);

        Assert.Equal(50, result.TotalRows);
        Assert.Equal(39, result.ImportedRows);
        Assert.Equal(11, result.RejectedRows);
        Assert.True(result.IsReconciled);
        Assert.Equal(result.TotalAmountInFile, result.ImportedAmount + result.RejectedAmount);

        // Every deliberately invalid line in the sample is reported with its line number and a reason.
        Assert.Equal(new[] { 6, 11, 15, 18, 21, 26, 30, 33, 37, 41, 42 }, result.Errors.Select(e => e.RowNumber));
        Assert.Contains("Unknown branch code 'MTR'", result.Errors.Single(e => e.RowNumber == 6).Reason, StringComparison.Ordinal);
        Assert.Contains("Unknown product code 'GOLD'", result.Errors.Single(e => e.RowNumber == 11).Reason, StringComparison.Ordinal);
        Assert.Contains("Duplicate reference", result.Errors.Single(e => e.RowNumber == 26).Reason, StringComparison.Ordinal);
        Assert.Contains("not a valid number", result.Errors.Single(e => e.RowNumber == 33).Reason, StringComparison.Ordinal);
        Assert.Contains("Personal Loan amounts must be between", result.Errors.Single(e => e.RowNumber == 42).Reason, StringComparison.Ordinal);

        // Valid rows were created as immutable legacy applications, each with an "Imported" history entry.
        var imported = await _host.QueryAsync(db => db.LoanApplications.Where(a => a.IsImported).Include(a => a.Actions).ToListAsync());
        Assert.Equal(39, imported.Count);
        Assert.Equal(before + 39, await _host.QueryAsync(db => db.LoanApplications.CountAsync()));
        Assert.All(imported, a => Assert.Single(a.Actions, x => x.ActionType == ApprovalActionType.Imported));
        Assert.All(imported, a => Assert.Equal(result.BatchId, a.ImportBatchId));

        // Two rows belong to the same legacy customer (same NIC): one customer record, two loans.
        var sharedNic = await _host.QueryAsync(db => db.Customers.CountAsync(c => c.Nic == "197917732597"));
        Assert.Equal(1, sharedNic);
    }

    [Fact]
    public async Task Same_file_cannot_be_imported_twice()
    {
        await _host.SignInAsync("admin@demo.lk");
        var bytes = await File.ReadAllBytesAsync(SamplePath);
        await ImportAsync(new MemoryStream(bytes));

        await Assert.ThrowsAsync<ConflictException>(() => ImportAsync(new MemoryStream(bytes)));
    }

    [Fact]
    public async Task File_without_required_columns_is_rejected()
    {
        await _host.SignInAsync("admin@demo.lk");
        var csv = new MemoryStream(Encoding.UTF8.GetBytes("LegacyReference,Amount\nLEG-2020-00001,100000\n"));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => ImportAsync(csv, "bad.csv"));
        Assert.Contains("Missing required column", ex.Errors["File"][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_admins_can_import()
    {
        await _host.SignInAsync("maker1@demo.lk");
        await using var file = File.OpenRead(SamplePath);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => ImportAsync(file));
    }
}
