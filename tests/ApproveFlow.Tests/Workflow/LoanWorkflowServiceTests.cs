using ApproveFlow.Application.Abstractions.Persistence;
using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Application.Common.Security;
using ApproveFlow.Application.LoanApplications;
using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApproveFlow.Tests.Workflow;

/// <summary>Service-level tests of the maker-checker workflow against a real (SQLite) database.</summary>
public sealed class LoanWorkflowServiceTests : IAsyncLifetime
{
    private SqliteTestHost _host = null!;

    public async Task InitializeAsync() => _host = await SqliteTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Maker_cannot_approve_own_application_even_when_also_holding_checker_role()
    {
        // Simulate a mis-configured account that holds both roles: the four-eyes rule must still hold.
        await _host.SignInAsync("maker1@demo.lk", AppRoles.Checker);
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 500_000m);
        await _host.SignInAsync("maker1@demo.lk", AppRoles.Checker);

        var ex = await Assert.ThrowsAsync<SegregationOfDutiesException>(() =>
            _host.ActAsync(application.Id, w => w.ApproveAsync, application.RowVersion));

        Assert.Equal("four_eyes_violation", ex.Code);
        Assert.Equal(LoanStatus.Submitted, (await _host.GetApplicationAsync(application.Id)).Status);
    }

    [Fact]
    public async Task Maker_role_alone_cannot_approve()
    {
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 500_000m);
        await _host.SignInAsync("maker2@demo.lk");

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => _host.ActAsync(application.Id, w => w.ApproveAsync, application.RowVersion));
    }

    [Fact]
    public async Task Checker_within_limit_approves_in_a_single_step()
    {
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 1_800_000m);
        await _host.SignInAsync("checker1@demo.lk");

        var result = await _host.ActAsync(application.Id, w => w.ApproveAsync, application.RowVersion);

        Assert.Equal(LoanStatus.Approved, result.Status);
    }

    [Fact]
    public async Task Checker_over_limit_triggers_dual_approval()
    {
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 5_000_000m);

        // checker1's limit is LKR 2,000,000: the approval is recorded as the first of two.
        await _host.SignInAsync("checker1@demo.lk");
        var first = await _host.ActAsync(application.Id, w => w.ApproveAsync, application.RowVersion, "Recommended for approval.");
        Assert.Equal(LoanStatus.PendingSecondApproval, first.Status);
        Assert.Contains("second checker", first.Message, StringComparison.OrdinalIgnoreCase);

        // The same checker cannot complete their own dual approval.
        var sameChecker = await Assert.ThrowsAsync<SegregationOfDutiesException>(() =>
            _host.ActAsync(application.Id, w => w.ApproveAsync, first.RowVersion));
        Assert.Equal("dual_approval_same_checker", sameChecker.Code);

        // A different checker completes it.
        await _host.SignInAsync("checker2@demo.lk");
        var second = await _host.ActAsync(application.Id, w => w.ApproveAsync, first.RowVersion, "Second approval.");
        Assert.Equal(LoanStatus.Approved, second.Status);

        var details = await _host.GetApplicationAsync(application.Id);
        Assert.Equal(
            new[] { ApprovalActionType.Created, ApprovalActionType.Submitted, ApprovalActionType.FirstApproval, ApprovalActionType.Approved },
            details.History.Select(h => h.ActionType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Too short")]
    public async Task Reject_without_adequate_remarks_fails(string? remarks)
    {
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 900_000m);
        await _host.SignInAsync("checker1@demo.lk");

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _host.ActAsync(application.Id, w => w.RejectAsync, application.RowVersion, remarks));

        Assert.True(ex.Errors.ContainsKey("Remarks"));
        Assert.Equal(LoanStatus.Submitted, (await _host.GetApplicationAsync(application.Id)).Status);
    }

    [Fact]
    public async Task Return_without_remarks_fails_and_with_remarks_succeeds()
    {
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 900_000m);
        await _host.SignInAsync("checker1@demo.lk");

        await Assert.ThrowsAsync<ValidationException>(() => _host.ActAsync(application.Id, w => w.ReturnForCorrectionAsync, application.RowVersion));
        var result = await _host.ActAsync(application.Id, w => w.ReturnForCorrectionAsync, application.RowVersion,
            "Please attach the latest salary slips.");

        Assert.Equal(LoanStatus.ReturnedForCorrection, result.Status);
    }

    [Fact]
    public async Task Approved_application_cannot_be_edited()
    {
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 900_000m);
        await _host.SignInAsync("checker1@demo.lk");
        var approved = await _host.ActAsync(application.Id, w => w.ApproveAsync, application.RowVersion);

        await _host.SignInAsync("maker1@demo.lk");
        var edit = new UpdateLoanApplicationRequest
        {
            CustomerId = application.CustomerId,
            LoanProductId = application.LoanProductId,
            Amount = 950_000m,
            TenureMonths = application.TenureMonths,
            InterestRate = application.InterestRate,
            Purpose = application.Purpose,
            MonthlyIncome = application.MonthlyIncome,
            ExistingObligations = application.ExistingObligations,
            RowVersion = approved.RowVersion,
        };

        var ex = await Assert.ThrowsAsync<InvalidStatusTransitionException>(() =>
            _host.WithScopeAsync(sp => sp.GetRequiredService<ILoanApplicationService>().UpdateAsync(application.Id, edit)));
        Assert.Equal("immutable_record", ex.Code);
        Assert.Equal(900_000m, (await _host.GetApplicationAsync(application.Id)).Amount);
    }

    [Fact]
    public async Task Stale_row_version_returns_record_was_modified_conflict()
    {
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 900_000m);

        // Both checkers opened the same version; checker2 acts first.
        await _host.SignInAsync("checker2@demo.lk");
        await _host.ActAsync(application.Id, w => w.ReturnForCorrectionAsync, application.RowVersion, "Missing valuation report.");

        await _host.SignInAsync("checker1@demo.lk");
        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            _host.ActAsync(application.Id, w => w.ApproveAsync, application.RowVersion));

        Assert.Contains("modified by another user", ex.Message, StringComparison.Ordinal);
        Assert.Equal(LoanStatus.ReturnedForCorrection, (await _host.GetApplicationAsync(application.Id)).Status);
    }

    [Fact]
    public async Task Simultaneous_saves_are_detected_by_the_database_row_version()
    {
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 900_000m);
        var checker1 = new Actor((await _host.SignInAsync("checker1@demo.lk")).Id, "Checker One");
        var checker2 = new Actor((await _host.SignInAsync("checker2@demo.lk")).Id, "Checker Two");

        // Two requests read the same row before either writes (a true race, below the service-level fast check).
        await using var scopeA = _host.Services.CreateAsyncScope();
        await using var scopeB = _host.Services.CreateAsyncScope();
        var repoA = scopeA.ServiceProvider.GetRequiredService<ILoanApplicationRepository>();
        var repoB = scopeB.ServiceProvider.GetRequiredService<ILoanApplicationRepository>();
        var copyA = (await repoA.GetForUpdateAsync(application.Id))!;
        var copyB = (await repoB.GetForUpdateAsync(application.Id))!;

        copyA.Approve(checker1, 2_000_000m, null, DateTime.UtcNow);
        await scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        copyB.Reject(checker2, "Rejecting a version that is already stale.", DateTime.UtcNow);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());

        Assert.Equal(LoanStatus.Approved, (await _host.GetApplicationAsync(application.Id)).Status);
    }

    [Fact]
    public async Task Every_state_change_creates_an_approval_action_with_actor_and_remarks()
    {
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 900_000m);
        var checker = await _host.SignInAsync("checker2@demo.lk");
        var returned = await _host.ActAsync(application.Id, w => w.ReturnForCorrectionAsync, application.RowVersion, "Attach the salary slips please.");

        var maker = await _host.SignInAsync("maker1@demo.lk");
        var resubmitted = await _host.ActAsync(application.Id, w => w.SubmitAsync, returned.RowVersion);

        await _host.SignInAsync("checker2@demo.lk");
        await _host.ActAsync(application.Id, w => w.ApproveAsync, resubmitted.RowVersion);

        var history = await _host.WithScopeAsync(sp => sp.GetRequiredService<ILoanApplicationService>().GetHistoryAsync(application.Id));
        Assert.Equal(
            new[]
            {
                ApprovalActionType.Created, ApprovalActionType.Submitted, ApprovalActionType.ReturnedForCorrection,
                ApprovalActionType.Resubmitted, ApprovalActionType.Approved,
            },
            history.Select(h => h.ActionType));
        Assert.Equal(checker.Id, history[2].PerformedById);
        Assert.Equal("Attach the salary slips please.", history[2].Remarks);
        Assert.Equal(maker.Id, history[3].PerformedById);
        Assert.All(history, h => Assert.NotEqual(default, h.PerformedAtUtc));
    }

    [Fact]
    public async Task Maker_can_only_edit_applications_they_own()
    {
        var application = await _host.CreateApplicationAsync("maker1@demo.lk", 900_000m, submit: false);
        await _host.SignInAsync("maker2@demo.lk");

        var ex = await Assert.ThrowsAsync<SegregationOfDutiesException>(() =>
            _host.ActAsync(application.Id, w => w.SubmitAsync, application.RowVersion));
        Assert.Equal("not_owner", ex.Code);
    }

    [Fact]
    public async Task Amount_outside_product_range_is_reported_against_the_amount_field()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _host.CreateApplicationAsync("maker1@demo.lk", 60_000_000m, submit: false));
        Assert.True(ex.Errors.ContainsKey("Amount"));
    }

    [Fact]
    public async Task Seeded_data_covers_every_status_and_reference_format()
    {
        var statuses = await _host.QueryAsync(db => db.LoanApplications.Select(a => a.Status).Distinct().ToListAsync());
        Assert.Equal(Enum.GetValues<LoanStatus>().OrderBy(s => s), statuses.OrderBy(s => s));

        var references = await _host.QueryAsync(db => db.LoanApplications.Select(a => a.ReferenceNumber).ToListAsync());
        Assert.Equal(60, references.Count);
        Assert.All(references, r => Assert.Matches(@"^LDB-\d{4}-\d{6}$", r));
    }
}
