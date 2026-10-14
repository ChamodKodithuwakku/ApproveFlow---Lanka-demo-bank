using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Rules;

namespace ApproveFlow.Domain.Entities;

/// <summary>Value object holding the financial terms of an application. Invariants are checked on construction.</summary>
public sealed record LoanTerms
{
    public LoanTerms(decimal amount, int tenureMonths, decimal interestRate, string purpose, decimal monthlyIncome, decimal existingObligations)
    {
        if (amount <= 0)
        {
            throw new BusinessRuleViolationException("amount_not_positive", "Loan amount must be greater than zero.");
        }

        if (tenureMonths < LoanPolicy.MinTenureMonths || tenureMonths > LoanPolicy.MaxTenureMonths)
        {
            throw new BusinessRuleViolationException("tenure_out_of_range",
                $"Tenure must be between {LoanPolicy.MinTenureMonths} and {LoanPolicy.MaxTenureMonths} months.");
        }

        if (interestRate < 0 || interestRate > LoanPolicy.MaxInterestRate)
        {
            throw new BusinessRuleViolationException("interest_rate_out_of_range", "Interest rate must be between 0 and 100%.");
        }

        if (monthlyIncome <= 0)
        {
            throw new BusinessRuleViolationException("income_not_positive", "Monthly income must be greater than zero.");
        }

        if (existingObligations < 0)
        {
            throw new BusinessRuleViolationException("obligations_negative", "Existing obligations cannot be negative.");
        }

        var trimmedPurpose = (purpose ?? string.Empty).Trim();
        if (trimmedPurpose.Length < LoanPolicy.MinPurposeLength || trimmedPurpose.Length > LoanPolicy.MaxPurposeLength)
        {
            throw new BusinessRuleViolationException("purpose_length",
                $"Purpose must be between {LoanPolicy.MinPurposeLength} and {LoanPolicy.MaxPurposeLength} characters.");
        }

        Amount = amount;
        TenureMonths = tenureMonths;
        InterestRate = interestRate;
        Purpose = trimmedPurpose;
        MonthlyIncome = monthlyIncome;
        ExistingObligations = existingObligations;
    }

    public decimal Amount { get; }
    public int TenureMonths { get; }
    public decimal InterestRate { get; }
    public string Purpose { get; }
    public decimal MonthlyIncome { get; }
    public decimal ExistingObligations { get; }

    public decimal MonthlyInstallment => LoanCalculator.MonthlyInstallment(Amount, InterestRate, TenureMonths);

    public decimal DebtBurdenRatio => LoanCalculator.DebtBurdenRatio(MonthlyIncome, ExistingObligations, MonthlyInstallment);
}
