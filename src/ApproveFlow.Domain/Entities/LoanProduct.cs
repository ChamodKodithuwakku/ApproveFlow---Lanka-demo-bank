using System.Globalization;
using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Rules;

namespace ApproveFlow.Domain.Entities;

public class LoanProduct : AuditableEntity
{
    private LoanProduct()
    {
    }

    public LoanProduct(string code, string name, string? description, decimal minAmount, decimal maxAmount,
        int minTenureMonths, int maxTenureMonths, decimal defaultInterestRate)
    {
        Update(code, name, description, minAmount, maxAmount, minTenureMonths, maxTenureMonths, defaultInterestRate, isActive: true);
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal MinAmount { get; private set; }
    public decimal MaxAmount { get; private set; }
    public int MinTenureMonths { get; private set; }
    public int MaxTenureMonths { get; private set; }
    public decimal DefaultInterestRate { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string code, string name, string? description, decimal minAmount, decimal maxAmount,
        int minTenureMonths, int maxTenureMonths, decimal defaultInterestRate, bool isActive)
    {
        if (minAmount <= 0 || maxAmount < minAmount)
        {
            throw new BusinessRuleViolationException("product_amount_range", "Product amount range is invalid (0 < minimum ≤ maximum).");
        }

        if (minTenureMonths < LoanPolicy.MinTenureMonths || maxTenureMonths > LoanPolicy.MaxTenureMonths || maxTenureMonths < minTenureMonths)
        {
            throw new BusinessRuleViolationException("product_tenure_range",
                $"Product tenure range must be within {LoanPolicy.MinTenureMonths}–{LoanPolicy.MaxTenureMonths} months and minimum ≤ maximum.");
        }

        if (defaultInterestRate < 0 || defaultInterestRate > LoanPolicy.MaxInterestRate)
        {
            throw new BusinessRuleViolationException("product_interest_rate", "Default interest rate must be between 0 and 100%.");
        }

        Code = Guard.Required(code, nameof(code)).ToUpperInvariant();
        Name = Guard.Required(name, nameof(name));
        Description = Guard.Optional(description);
        MinAmount = minAmount;
        MaxAmount = maxAmount;
        MinTenureMonths = minTenureMonths;
        MaxTenureMonths = maxTenureMonths;
        DefaultInterestRate = defaultInterestRate;
        IsActive = isActive;
    }

    /// <summary>Throws when the requested amount or tenure is outside this product's limits.</summary>
    public void EnsureWithinLimits(decimal amount, int tenureMonths)
    {
        if (amount < MinAmount || amount > MaxAmount)
        {
            throw new BusinessRuleViolationException("amount_outside_product_range",
                string.Create(CultureInfo.InvariantCulture,
                    $"{Name} amounts must be between LKR {MinAmount:N2} and LKR {MaxAmount:N2}."));
        }

        if (tenureMonths < MinTenureMonths || tenureMonths > MaxTenureMonths)
        {
            throw new BusinessRuleViolationException("tenure_outside_product_range",
                $"{Name} tenure must be between {MinTenureMonths} and {MaxTenureMonths} months.");
        }
    }
}
