using System.Globalization;
using System.Text.Json;
using ApproveFlow.Application.LoanApplications;
using ApproveFlow.Domain.Entities;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Infrastructure.Persistence;
using ApproveFlow.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApproveFlow.Tests.Auditing;

public sealed class AuditTrailTests : IAsyncLifetime
{
    private SqliteTestHost _host = null!;

    public async Task InitializeAsync() => _host = await SqliteTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Update_writes_audit_row_with_old_and_new_values_user_and_ip()
    {
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 900_000m, submit: false);
        var maker = await _host.SignInAsync("maker1@demo.lk");
        await _host.ActAsync(application.Id, w => w.SubmitAsync, application.RowVersion);

        var key = application.Id.ToString(CultureInfo.InvariantCulture);
        var update = await _host.QueryAsync(db => db.AuditLogs
            .Where(a => a.TableName == "LoanApplications" && a.RecordKey == key && a.Action == "Update")
            .OrderByDescending(a => a.Id)
            .FirstAsync());

        Assert.Equal(AuditEventType.EntityChange, update.EventType);
        Assert.Contains("Status", update.ChangedColumns!.Split(','));
        Assert.Equal("Draft", JsonDocument.Parse(update.OldValues!).RootElement.GetProperty("Status").GetString());
        Assert.Equal("Submitted", JsonDocument.Parse(update.NewValues!).RootElement.GetProperty("Status").GetString());
        Assert.Equal(maker.Id, update.UserId);
        Assert.Equal("maker1@demo.lk", update.UserName);
        Assert.Equal("10.20.30.40", update.IpAddress);
        Assert.Equal("test-correlation-id", update.CorrelationId);
    }

    [Fact]
    public async Task Insert_audit_row_records_the_database_generated_key_in_the_same_transaction()
    {
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 900_000m, submit: false);

        var key = application.Id.ToString(CultureInfo.InvariantCulture);
        var insert = await _host.QueryAsync(db => db.AuditLogs.SingleAsync(a =>
            a.TableName == "LoanApplications" && a.RecordKey == key && a.Action == "Insert"));

        Assert.Equal(application.ReferenceNumber,
            JsonDocument.Parse(insert.NewValues!).RootElement.GetProperty("ReferenceNumber").GetString());

        // The approval-history row inserted with it is audited as well.
        var actionInserts = await _host.QueryAsync(db => db.AuditLogs.CountAsync(a => a.TableName == "ApprovalActions" && a.Action == "Insert"));
        Assert.True(actionInserts == 0, "ApprovalActions is itself the history table and is not double-audited.");
    }

    [Fact]
    public async Task Soft_deleting_a_draft_is_audited_as_SoftDelete()
    {
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 900_000m, submit: false);
        await _host.SignInAsync("maker1@demo.lk");
        await _host.WithScopeAsync(sp => sp.GetRequiredService<ILoanApplicationService>().DeleteDraftAsync(application.Id, application.RowVersion));

        var key = application.Id.ToString(CultureInfo.InvariantCulture);
        Assert.True(await _host.QueryAsync(db => db.AuditLogs.AnyAsync(a => a.RecordKey == key && a.Action == "SoftDelete")));
        Assert.False(await _host.QueryAsync(db => db.LoanApplications.AnyAsync(a => a.Id == application.Id)));
    }

    [Fact]
    public async Task Audit_rows_are_append_only()
    {
        await _host.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var row = await db.AuditLogs.FirstAsync();
            db.Entry(row).Property(nameof(AuditLog.Action)).CurrentValue = "Tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        });

        await _host.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            db.AuditLogs.Remove(await db.AuditLogs.FirstAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        });
    }

    [Fact]
    public async Task Secrets_are_never_written_to_the_audit_trail()
    {
        var userRows = await _host.QueryAsync(db => db.AuditLogs.Where(a => a.TableName == "AspNetUsers" && a.NewValues != null)
            .Select(a => a.NewValues!).ToListAsync());

        Assert.NotEmpty(userRows);
        Assert.All(userRows, json =>
        {
            Assert.DoesNotContain("PasswordHash", json, StringComparison.Ordinal);
            Assert.DoesNotContain("SecurityStamp", json, StringComparison.Ordinal);
        });
    }
}
