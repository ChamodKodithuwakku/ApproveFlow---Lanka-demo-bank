using ApproveFlow.Application.Common.Security;
using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Entities;
using ApproveFlow.Domain.Enums;

namespace ApproveFlow.Tests.Domain;

/// <summary>Pure domain tests of the workflow rules (no database).</summary>
public class LoanApplicationTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 4, 30, 0, DateTimeKind.Utc);
    private static readonly Actor Maker = new("maker-1", "Maker One");
    private static readonly Actor OtherMaker = new("maker-2", "Maker Two");
    private static readonly Actor Checker1 = new("checker-1", "Checker One");
    private static readonly Actor Checker2 = new("checker-2", "Checker Two");

    private static LoanApplication NewDraft(decimal amount = 1_500_000m)
    {
        var branch = new Branch("COL", "Colombo Main Branch", "Colombo");
        var product = new LoanProduct("HL", "Housing Loan", null, 100_000m, 50_000_000m, 12, 300, 11.5m);
        var customer = new Customer("CIF000001", "Test Customer", "199012345678", 1);
        var terms = new LoanTerms(amount, 120, 11.5m, "Construction of a family house", 400_000m, 20_000m);
        return LoanApplication.CreateDraft("LDB-2026-000001", customer, product, branch, terms, Maker, Now);
    }

    private static LoanApplication NewSubmitted(decimal amount = 1_500_000m)
    {
        var application = NewDraft(amount);
        application.Submit(Maker, Now.AddMinutes(5));
        return application;
    }

    [Fact]
    public void New_draft_calculates_emi_and_dbr_and_records_creation()
    {
        var application = NewDraft();

        Assert.Equal(LoanStatus.Draft, application.Status);
        Assert.True(application.MonthlyInstallment > 0);
        Assert.Equal(Math.Round((20_000m + application.MonthlyInstallment) / 400_000m * 100m, 2), application.DebtBurdenRatio);
        Assert.Single(application.Actions, a => a.ActionType == ApprovalActionType.Created);
    }

    [Fact]
    public void Only_the_owning_maker_can_submit()
    {
        var application = NewDraft();
        var ex = Assert.Throws<SegregationOfDutiesException>(() => application.Submit(OtherMaker, Now));
        Assert.Equal("not_owner", ex.Code);
    }

    [Fact]
    public void Creator_cannot_approve_own_application_even_with_checker_rights()
    {
        var application = NewSubmitted();
        var ex = Assert.Throws<SegregationOfDutiesException>(() => application.Approve(Maker, 10_000_000m, null, Now));
        Assert.Equal("four_eyes_violation", ex.Code);
        Assert.Equal(LoanStatus.Submitted, application.Status);
    }

    [Fact]
    public void Amount_within_limit_is_approved_by_a_single_checker()
    {
        var application = NewSubmitted(1_500_000m);
        Assert.Equal(ApprovalOutcome.Approved, application.Approve(Checker1, 2_000_000m, null, Now));
        Assert.Equal(LoanStatus.Approved, application.Status);
        Assert.Equal(Checker1.UserId, application.DecidedById);
    }

    [Fact]
    public void Amount_above_limit_requires_a_second_different_checker()
    {
        var application = NewSubmitted(5_000_000m);

        Assert.Equal(ApprovalOutcome.PendingSecondApproval, application.Approve(Checker1, 2_000_000m, null, Now));
        Assert.Equal(LoanStatus.PendingSecondApproval, application.Status);

        var sameChecker = Assert.Throws<SegregationOfDutiesException>(() => application.Approve(Checker1, 2_000_000m, null, Now));
        Assert.Equal("dual_approval_same_checker", sameChecker.Code);

        Assert.Equal(ApprovalOutcome.Approved, application.Approve(Checker2, 10_000_000m, null, Now));
        Assert.Equal(LoanStatus.Approved, application.Status);
        Assert.Contains(application.Actions, a => a.ActionType == ApprovalActionType.FirstApproval && a.PerformedById == Checker1.UserId);
        Assert.Contains(application.Actions, a => a.ActionType == ApprovalActionType.Approved && a.PerformedById == Checker2.UserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  short  ")]
    public void Reject_and_return_require_remarks_of_at_least_ten_characters(string? remarks)
    {
        var application = NewSubmitted();
        Assert.Equal("remarks_required", Assert.Throws<BusinessRuleViolationException>(() => application.Reject(Checker1, remarks, Now)).Code);
        Assert.Equal("remarks_required", Assert.Throws<BusinessRuleViolationException>(() => application.ReturnForCorrection(Checker1, remarks, Now)).Code);
        Assert.Equal(LoanStatus.Submitted, application.Status);
    }

    [Fact]
    public void Approved_and_rejected_applications_are_immutable()
    {
        var approved = NewSubmitted();
        approved.Approve(Checker1, 2_000_000m, null, Now);
        var rejected = NewSubmitted();
        rejected.Reject(Checker1, "Debt-burden ratio above policy threshold.", Now);

        foreach (var application in new[] { approved, rejected })
        {
            Assert.Equal("immutable_record", Assert.Throws<InvalidStatusTransitionException>(() => application.Submit(Maker, Now)).Code);
            Assert.Equal("immutable_record", Assert.Throws<InvalidStatusTransitionException>(() => application.Approve(Checker2, 10_000_000m, null, Now)).Code);
            Assert.Equal("immutable_record", Assert.Throws<InvalidStatusTransitionException>(() => application.Reject(Checker2, "Trying to change a final decision.", Now)).Code);
        }
    }

    [Fact]
    public void Returned_application_can_be_corrected_and_resubmitted_and_first_approval_is_cleared()
    {
        var application = NewSubmitted(5_000_000m);
        application.Approve(Checker1, 2_000_000m, null, Now);
        application.ReturnForCorrection(Checker2, "Please attach the valuation report.", Now);

        Assert.Equal(LoanStatus.ReturnedForCorrection, application.Status);
        Assert.Null(application.FirstApproverId);

        application.Submit(Maker, Now.AddHours(1));
        Assert.Equal(LoanStatus.Submitted, application.Status);
        Assert.Equal(ApprovalActionType.Resubmitted, application.Actions.Last().ActionType);
    }

    [Fact]
    public void Each_state_change_appends_one_history_entry()
    {
        var application = NewSubmitted(5_000_000m);
        application.Approve(Checker1, 2_000_000m, "Recommended.", Now);
        application.Approve(Checker2, 10_000_000m, "Approved.", Now);

        Assert.Equal(
            new[] { ApprovalActionType.Created, ApprovalActionType.Submitted, ApprovalActionType.FirstApproval, ApprovalActionType.Approved },
            application.Actions.Select(a => a.ActionType));
    }

    [Theory]
    [InlineData(new[] { AppRoles.Maker, AppRoles.Checker }, false)]
    [InlineData(new[] { AppRoles.Auditor, AppRoles.Admin }, false)]
    [InlineData(new[] { AppRoles.Admin, AppRoles.Maker }, true)]
    [InlineData(new[] { AppRoles.Checker }, true)]
    public void Role_assignment_enforces_segregation_of_duties(string[] roles, bool valid)
    {
        Assert.Equal(valid, AppRoles.ValidateCombination(roles) is null);
    }
}
