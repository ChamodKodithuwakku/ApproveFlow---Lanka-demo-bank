using System.Globalization;
using ApproveFlow.Application.Common.Time;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Domain.Rules;

namespace ApproveFlow.Web.Infrastructure;

/// <summary>Presentation formatting: LKR amounts, Asia/Colombo times, status labels and badge styles.</summary>
public static class Display
{
    private static readonly CultureInfo Numbers = CultureInfo.GetCultureInfo("en-US");

    public static string Money(decimal amount) => "LKR " + amount.ToString("N2", Numbers);

    public static string Money(decimal? amount) => amount.HasValue ? Money(amount.Value) : "—";

    public static string Number(decimal value, int decimals = 2) => value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), Numbers);

    public static string Percent(decimal value) => value.ToString("0.00", Numbers) + "%";

    public static string Time(DateTime utc) => ColomboTime.Format(utc);

    public static string Time(DateTime? utc) => ColomboTime.Format(utc);

    public static string Date(DateTime utc) =>
        ColomboTime.FromUtc(utc).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    public static string StatusLabel(LoanStatus status) => status switch
    {
        LoanStatus.PendingSecondApproval => "Pending 2nd approval",
        LoanStatus.ReturnedForCorrection => "Returned",
        _ => status.ToString(),
    };

    public static string StatusCss(LoanStatus status) => status switch
    {
        LoanStatus.Draft => "af-status af-status-draft",
        LoanStatus.Submitted => "af-status af-status-submitted",
        LoanStatus.PendingSecondApproval => "af-status af-status-pending2",
        LoanStatus.Approved => "af-status af-status-approved",
        LoanStatus.Rejected => "af-status af-status-rejected",
        LoanStatus.ReturnedForCorrection => "af-status af-status-returned",
        _ => "af-status",
    };

    public static string StatusIcon(LoanStatus status) => status switch
    {
        LoanStatus.Draft => "bi-pencil-square",
        LoanStatus.Submitted => "bi-send",
        LoanStatus.PendingSecondApproval => "bi-people",
        LoanStatus.Approved => "bi-check-circle",
        LoanStatus.Rejected => "bi-x-circle",
        LoanStatus.ReturnedForCorrection => "bi-arrow-return-left",
        _ => "bi-circle",
    };

    public static string ActionLabel(ApprovalActionType type) => type switch
    {
        ApprovalActionType.Created => "Draft created",
        ApprovalActionType.Submitted => "Submitted for approval",
        ApprovalActionType.Resubmitted => "Resubmitted after correction",
        ApprovalActionType.FirstApproval => "First approval (dual control)",
        ApprovalActionType.Approved => "Approved",
        ApprovalActionType.Rejected => "Rejected",
        ApprovalActionType.ReturnedForCorrection => "Returned for correction",
        ApprovalActionType.Imported => "Migrated from legacy system",
        _ => type.ToString(),
    };

    public static string ActionCss(ApprovalActionType type) => type switch
    {
        ApprovalActionType.Approved => "af-timeline-approved",
        ApprovalActionType.FirstApproval => "af-timeline-pending2",
        ApprovalActionType.Rejected => "af-timeline-rejected",
        ApprovalActionType.ReturnedForCorrection => "af-timeline-returned",
        ApprovalActionType.Submitted or ApprovalActionType.Resubmitted => "af-timeline-submitted",
        _ => "af-timeline-neutral",
    };

    public static string DbrCss(decimal ratio) =>
        ratio > LoanPolicy.HighDebtBurdenRatio ? "af-dbr-high" : ratio > LoanPolicy.ModerateDebtBurdenRatio ? "af-dbr-moderate" : "af-dbr-low";

    public static string DbrLabel(decimal ratio) =>
        ratio > LoanPolicy.HighDebtBurdenRatio ? "High" : ratio > LoanPolicy.ModerateDebtBurdenRatio ? "Moderate" : "Low";

    /// <summary>Masks all but the last four characters of an identifier (used in list views).</summary>
    public static string Mask(string? value) =>
        string.IsNullOrEmpty(value) || value.Length <= 4 ? value ?? string.Empty : new string('•', value.Length - 4) + value[^4..];

    public static int AgeDays(DateTime? submittedUtc, DateTime nowUtc) =>
        submittedUtc.HasValue ? Math.Max(0, (int)((nowUtc - submittedUtc.Value).TotalHours / 24)) : 0;

    public static string AgeCss(int days) => days <= 2 ? "af-age-fresh" : days <= 5 ? "af-age-warn" : "af-age-late";
}
