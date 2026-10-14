namespace ApproveFlow.Domain.Common;

internal static class Guard
{
    public static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BusinessRuleViolationException("required", $"{parameterName} is required.");
        }

        return value.Trim();
    }

    public static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
