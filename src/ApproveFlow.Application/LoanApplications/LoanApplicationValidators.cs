using ApproveFlow.Application.Common.Validation;
using ApproveFlow.Domain.Rules;
using FluentValidation;

namespace ApproveFlow.Application.LoanApplications;

/// <summary>Rules shared by create and update requests.</summary>
public abstract class LoanApplicationInputValidator<T> : AbstractValidator<T> where T : CreateLoanApplicationRequest
{
    protected LoanApplicationInputValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("Select a customer.");
        RuleFor(x => x.LoanProductId).GreaterThan(0).WithMessage("Select a loan product.");
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(1_000_000_000m)
            .WithMessage("Amount must be greater than zero and at most LKR 1,000,000,000.");
        RuleFor(x => x.TenureMonths).InclusiveBetween(LoanPolicy.MinTenureMonths, LoanPolicy.MaxTenureMonths);
        RuleFor(x => x.InterestRate).InclusiveBetween(0m, LoanPolicy.MaxInterestRate);
        RuleFor(x => x.Purpose).NotEmpty().Length(LoanPolicy.MinPurposeLength, LoanPolicy.MaxPurposeLength);
        RuleFor(x => x.MonthlyIncome).GreaterThan(0).WithMessage("Monthly income must be greater than zero.");
        RuleFor(x => x.ExistingObligations).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateLoanApplicationRequestValidator : LoanApplicationInputValidator<CreateLoanApplicationRequest>
{
}

public sealed class UpdateLoanApplicationRequestValidator : LoanApplicationInputValidator<UpdateLoanApplicationRequest>
{
    public UpdateLoanApplicationRequestValidator()
    {
        RuleFor(x => x.RowVersion).MustBeRowVersion();
    }
}

public sealed class WorkflowActionRequestValidator : AbstractValidator<WorkflowActionRequest>
{
    public WorkflowActionRequestValidator()
    {
        RuleFor(x => x.RowVersion).MustBeRowVersion();
        RuleFor(x => x.Remarks).MaximumLength(LoanPolicy.MaxRemarksLength);

        // Reject and Return require remarks; selected with ValidationContext rule sets.
        RuleSet(WorkflowRuleSets.RemarksRequired, () =>
        {
            RuleFor(x => x.Remarks)
                .Must(r => (r?.Trim().Length ?? 0) >= LoanPolicy.MinRemarksLength)
                .WithMessage($"Remarks are mandatory and must be at least {LoanPolicy.MinRemarksLength} characters.");
        });
    }
}

public static class WorkflowRuleSets
{
    public const string RemarksRequired = "RemarksRequired";
}
