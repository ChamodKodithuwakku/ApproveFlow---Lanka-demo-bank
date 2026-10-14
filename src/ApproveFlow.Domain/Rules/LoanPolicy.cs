namespace ApproveFlow.Domain.Rules;

/// <summary>Bank-wide policy constants used by the domain and validators.</summary>
public static class LoanPolicy
{
    public const int MinRemarksLength = 10;
    public const int MaxRemarksLength = 1000;
    public const int MinTenureMonths = 1;
    public const int MaxTenureMonths = 360;
    public const decimal MaxInterestRate = 100m;
    public const int MinPurposeLength = 10;
    public const int MaxPurposeLength = 500;

    /// <summary>Debt-burden ratio (%) above which an application is flagged as high risk.</summary>
    public const decimal HighDebtBurdenRatio = 60m;

    /// <summary>Debt-burden ratio (%) above which an application is flagged as moderate risk.</summary>
    public const decimal ModerateDebtBurdenRatio = 40m;

    /// <summary>Reference number prefix for applications originated in ApproveFlow (LDB-YYYY-000001).</summary>
    public const string ReferencePrefix = "LDB";

    public static string FormatReference(int year, long sequence) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{ReferencePrefix}-{year:D4}-{sequence:D6}");
}
