using ApproveFlow.Domain.Rules;

namespace ApproveFlow.Tests.Domain;

public class LoanCalculatorTests
{
    [Fact]
    public void Monthly_installment_matches_standard_amortisation_formula()
    {
        // LKR 1,000,000 at 12% p.a. over 12 months: r = 1%, EMI = 88,848.79
        Assert.Equal(88_848.79m, LoanCalculator.MonthlyInstallment(1_000_000m, 12m, 12));
    }

    [Fact]
    public void Monthly_installment_with_zero_interest_is_straight_line()
    {
        Assert.Equal(100_000m, LoanCalculator.MonthlyInstallment(1_200_000m, 0m, 12));
    }

    [Fact]
    public void Debt_burden_ratio_includes_existing_obligations()
    {
        // (20,000 + 88,848.79) / 200,000 = 54.42%
        Assert.Equal(54.42m, LoanCalculator.DebtBurdenRatio(200_000m, 20_000m, 88_848.79m));
    }

    [Theory]
    [InlineData(0, 12, 12)]
    [InlineData(1000, -1, 12)]
    [InlineData(1000, 12, 0)]
    public void Invalid_inputs_are_rejected(decimal principal, decimal rate, int tenure)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LoanCalculator.MonthlyInstallment(principal, rate, tenure));
    }

    [Fact]
    public void Reference_numbers_use_the_LDB_year_sequence_format()
    {
        Assert.Equal("LDB-2026-000042", LoanPolicy.FormatReference(2026, 42));
    }
}
