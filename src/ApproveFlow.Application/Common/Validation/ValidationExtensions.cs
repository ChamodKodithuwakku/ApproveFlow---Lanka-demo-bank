using ApproveFlow.Domain.Common;
using FluentValidation;
using ValidationException = ApproveFlow.Application.Common.Exceptions.ValidationException;

namespace ApproveFlow.Application.Common.Validation;

public static class ValidationExtensions
{
    /// <summary>Runs FluentValidation and throws the application's <see cref="ValidationException"/> (HTTP 422) on failure.</summary>
    public static async Task ValidateOrThrowAsync<T>(this IValidator<T> validator, T instance, CancellationToken cancellationToken = default,
        params string[] ruleSets)
    {
        ArgumentNullException.ThrowIfNull(validator);
        var result = ruleSets.Length == 0
            ? await validator.ValidateAsync(instance, cancellationToken)
            : await validator.ValidateAsync(instance, options => options.IncludeRuleSets(ruleSets).IncludeRulesNotInRuleSet(), cancellationToken);

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors
                .GroupBy(e => e.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray(), StringComparer.OrdinalIgnoreCase));
        }
    }

    public static IRuleBuilderOptions<T, string> MustBeRowVersion<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("RowVersion is required (read the record first).")
            .Must(value => RowVersions.TryParse(value, out _)).WithMessage("RowVersion is not a valid concurrency token.");

    /// <summary>
    /// Runs a domain operation and converts business-rule violations about a specific input into field-level
    /// validation errors, so forms can highlight the offending field.
    /// </summary>
    public static T MapDomainErrors<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            return operation();
        }
        catch (BusinessRuleViolationException ex) when (FieldFor(ex.Code) is { } field)
        {
            throw new ValidationException(field, ex.Message);
        }
    }

    public static void MapDomainErrors(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        MapDomainErrors<bool>(() =>
        {
            operation();
            return true;
        });
    }

    private static string? FieldFor(string code) => code switch
    {
        _ when code.StartsWith("amount", StringComparison.Ordinal) => "Amount",
        _ when code.StartsWith("tenure", StringComparison.Ordinal) => "TenureMonths",
        _ when code.StartsWith("interest_rate", StringComparison.Ordinal) => "InterestRate",
        _ when code.StartsWith("income", StringComparison.Ordinal) => "MonthlyIncome",
        _ when code.StartsWith("obligations", StringComparison.Ordinal) => "ExistingObligations",
        _ when code.StartsWith("purpose", StringComparison.Ordinal) => "Purpose",
        _ when code.StartsWith("remarks", StringComparison.Ordinal) => "Remarks",
        "customer_inactive" => "CustomerId",
        "product_inactive" => "LoanProductId",
        _ => null,
    };
}

/// <summary>RowVersion values travel to clients as Base64 strings.</summary>
public static class RowVersions
{
    public static string Encode(byte[]? rowVersion) => rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : string.Empty;

    public static bool TryParse(string? value, out byte[] rowVersion)
    {
        rowVersion = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var buffer = new byte[64];
        if (Convert.TryFromBase64String(value.Trim(), buffer, out var written) && written is > 0 and <= 32)
        {
            rowVersion = buffer[..written];
            return true;
        }

        return false;
    }
}
