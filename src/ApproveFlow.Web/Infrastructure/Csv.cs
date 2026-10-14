namespace ApproveFlow.Web.Infrastructure;

/// <summary>RFC 4180 CSV output with protection against spreadsheet formula injection (OWASP "CSV injection").</summary>
public static class Csv
{
    private static readonly char[] FormulaPrefixes = { '=', '+', '-', '@', '\t', '\r' };
    private static readonly char[] QuoteTriggers = { ',', '"', '\n', '\r' };

    public static string Row(params string?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return string.Join(",", values.Select(Escape));
    }

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // A cell starting with =, +, -, @ could be executed as a formula by Excel; prefix with an apostrophe.
        if (Array.IndexOf(FormulaPrefixes, value[0]) >= 0)
        {
            value = "'" + value;
        }

        return value.IndexOfAny(QuoteTriggers) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
