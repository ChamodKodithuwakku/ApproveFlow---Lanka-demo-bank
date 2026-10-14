namespace ApproveFlow.Domain.Rules;

/// <summary>Pure financial calculations. Uses decimal arithmetic throughout to avoid binary floating-point error.</summary>
public static class LoanCalculator
{
    /// <summary>
    /// Equated monthly instalment: P × r × (1 + r)^n / ((1 + r)^n − 1), where r is the monthly rate.
    /// </summary>
    public static decimal MonthlyInstallment(decimal principal, decimal annualInterestRatePercent, int tenureMonths)
    {
        if (principal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(principal), "Principal must be positive.");
        }

        if (tenureMonths <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tenureMonths), "Tenure must be positive.");
        }

        if (annualInterestRatePercent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(annualInterestRatePercent), "Interest rate cannot be negative.");
        }

        if (annualInterestRatePercent == 0)
        {
            return Math.Round(principal / tenureMonths, 2, MidpointRounding.AwayFromZero);
        }

        var monthlyRate = annualInterestRatePercent / 1200m;
        var growth = 1m;
        for (var i = 0; i < tenureMonths; i++)
        {
            growth *= 1m + monthlyRate;
        }

        var installment = principal * monthlyRate * growth / (growth - 1m);
        return Math.Round(installment, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Debt-burden ratio (%) = (existing monthly obligations + new instalment) / monthly income × 100.</summary>
    public static decimal DebtBurdenRatio(decimal monthlyIncome, decimal existingObligations, decimal newInstallment)
    {
        if (monthlyIncome <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monthlyIncome), "Monthly income must be positive.");
        }

        return Math.Round((existingObligations + newInstallment) / monthlyIncome * 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Loan amount expressed as a multiple of annual income.</summary>
    public static decimal LoanToAnnualIncome(decimal amount, decimal monthlyIncome) =>
        monthlyIncome <= 0 ? 0 : Math.Round(amount / (monthlyIncome * 12m), 2, MidpointRounding.AwayFromZero);
}
