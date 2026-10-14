using System.Globalization;
using ApproveFlow.Application.Abstractions.Auditing;
using ApproveFlow.Application.Abstractions.Persistence;
using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Application.Common.Security;
using ApproveFlow.Domain.Entities;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace ApproveFlow.Application.AiRisk;

/// <summary>
/// The only data that may leave the bank for AI analysis. Privacy by construction: this type has no field for the
/// customer's name, NIC, phone, address or any free text (the loan purpose can contain personal details).
/// </summary>
public sealed record RiskSummaryInput(
    string ProductCode,
    string ProductName,
    decimal Amount,
    int TenureMonths,
    decimal InterestRatePercent,
    decimal MonthlyIncome,
    decimal ExistingMonthlyObligations,
    decimal MonthlyInstallment,
    decimal DebtBurdenRatioPercent,
    decimal LoanToAnnualIncome,
    int ProductMaxTenureMonths,
    decimal ProductDefaultInterestRatePercent,
    string CurrentStatus)
{
    public const string Currency = "LKR";

    public static RiskSummaryInput From(LoanApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var product = application.LoanProduct ?? throw new InvalidOperationException("Loan product must be loaded.");
        return new RiskSummaryInput(product.Code, product.Name, application.Amount, application.TenureMonths, application.InterestRate,
            application.MonthlyIncome, application.ExistingObligations, application.MonthlyInstallment, application.DebtBurdenRatio,
            LoanCalculator.LoanToAnnualIncome(application.Amount, application.MonthlyIncome), product.MaxTenureMonths,
            product.DefaultInterestRate, application.Status.ToString());
    }
}

/// <param name="IsFallback">True when the deterministic rule-based assessment was used instead of the LLM.</param>
/// <param name="Source">Human-readable origin, e.g. "Anthropic claude-opus-5-5" or "Rule-based assessment".</param>
/// <param name="FallbackReason">Why the LLM was not used (no key configured, service unavailable, refusal...).</param>
public sealed record RiskSummaryResult(
    RiskLevel RiskLevel,
    string Summary,
    IReadOnlyList<string> Factors,
    bool IsFallback,
    string Source,
    string? Model,
    string? FallbackReason,
    DateTime GeneratedAtUtc);

/// <summary>Produces an advisory risk summary. The implementation (Infrastructure) calls an LLM over HTTP.</summary>
public interface IAiRiskSummaryService
{
    Task<RiskSummaryResult> SummariseAsync(RiskSummaryInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic, explainable assessment based on bank policy thresholds. Used when no LLM is configured or the LLM
/// call fails, so the feature always works; also passed to the LLM as reference indicators.
/// </summary>
public static class RuleBasedRiskAssessor
{
    public const string SourceName = "Rule-based assessment";

    public static RiskSummaryResult Assess(RiskSummaryInput input, DateTime nowUtc, string? fallbackReason = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var factors = new List<(RiskLevel Level, string Text)>();
        var inv = CultureInfo.InvariantCulture;

        var dbr = input.DebtBurdenRatioPercent;
        factors.Add(dbr > LoanPolicy.HighDebtBurdenRatio
            ? (RiskLevel.High, string.Create(inv, $"Debt-burden ratio of {dbr:0.##}% exceeds the {LoanPolicy.HighDebtBurdenRatio:0}% policy threshold."))
            : dbr > LoanPolicy.ModerateDebtBurdenRatio
                ? (RiskLevel.Moderate, string.Create(inv, $"Debt-burden ratio of {dbr:0.##}% is within policy but above the {LoanPolicy.ModerateDebtBurdenRatio:0}% comfort level."))
                : (RiskLevel.Low, string.Create(inv, $"Debt-burden ratio of {dbr:0.##}% indicates comfortable repayment capacity.")));

        // Secured housing loans are normally larger relative to income than unsecured lending.
        var isHousing = string.Equals(input.ProductCode, "HL", StringComparison.OrdinalIgnoreCase);
        var (highMultiple, moderateMultiple) = isHousing ? (8m, 5m) : (5m, 3m);
        var lti = input.LoanToAnnualIncome;
        factors.Add(lti > highMultiple
            ? (RiskLevel.High, string.Create(inv, $"Loan amount is {lti:0.#}x annual income, above the {highMultiple:0}x guide for this product."))
            : lti > moderateMultiple
                ? (RiskLevel.Moderate, string.Create(inv, $"Loan amount is {lti:0.#}x annual income; verify income stability."))
                : (RiskLevel.Low, string.Create(inv, $"Loan amount is {lti:0.#}x annual income, proportionate to earnings.")));

        if (input.ProductMaxTenureMonths > 0 && input.TenureMonths > input.ProductMaxTenureMonths * 0.85m)
        {
            factors.Add((RiskLevel.Moderate, string.Create(inv,
                $"Tenure of {input.TenureMonths} months is close to the product maximum of {input.ProductMaxTenureMonths}, increasing interest cost and exposure period.")));
        }

        var rateGap = input.InterestRatePercent - input.ProductDefaultInterestRatePercent;
        if (rateGap > 1.5m)
        {
            factors.Add((RiskLevel.Moderate, string.Create(inv,
                $"Interest rate is {rateGap:0.##} percentage points above the product standard, which may reflect a risk premium.")));
        }
        else if (rateGap < -0.5m)
        {
            factors.Add((RiskLevel.Low, string.Create(inv,
                $"Interest rate is {-rateGap:0.##} percentage points below the product standard (concessionary pricing should be authorised).")));
        }

        var disposable = input.MonthlyIncome - input.ExistingMonthlyObligations - input.MonthlyInstallment;
        factors.Add(disposable < input.MonthlyIncome * 0.25m
            ? (RiskLevel.High, string.Create(inv, $"Only {RiskSummaryInput.Currency} {disposable:N0} of monthly income remains after all repayments."))
            : (RiskLevel.Low, string.Create(inv, $"{RiskSummaryInput.Currency} {disposable:N0} of monthly income remains after all repayments.")));

        var overall = factors.Max(f => f.Level);
        var summary = string.Create(inv,
                $"{overall} risk on the figures provided. A {input.ProductName} of {RiskSummaryInput.Currency} {input.Amount:N0} over {input.TenureMonths} months ")
            + string.Create(inv, $"has an instalment of {RiskSummaryInput.Currency} {input.MonthlyInstallment:N0}, giving a debt-burden ratio of {dbr:0.##}%. ")
            + (overall == RiskLevel.High
                ? "Repayment capacity is stretched; the checker should seek mitigants (lower amount, longer documented income history, security) or decline."
                : overall == RiskLevel.Moderate
                    ? "The application is within policy but merits verification of income and existing obligations."
                    : "Repayment capacity appears adequate on standard policy measures.");

        return new RiskSummaryResult(overall, summary, factors.OrderByDescending(f => f.Level).Select(f => f.Text).ToList(), IsFallback: true,
            SourceName, Model: null, fallbackReason, nowUtc);
    }
}

public interface IRiskSummaryAppService
{
    Task<RiskSummaryResult> GenerateAsync(int applicationId, CancellationToken cancellationToken = default);
}

/// <summary>Checker/Auditor use case: build the anonymised input, ask the AI service and record the request in the audit trail.</summary>
internal sealed class RiskSummaryAppService : IRiskSummaryAppService
{
    private readonly ILoanApplicationRepository _applications;
    private readonly IAiRiskSummaryService _ai;
    private readonly IAuditTrail _auditTrail;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RiskSummaryAppService> _logger;

    public RiskSummaryAppService(ILoanApplicationRepository applications, IAiRiskSummaryService ai, IAuditTrail auditTrail, ICurrentUser currentUser,
        ILogger<RiskSummaryAppService> logger)
    {
        _applications = applications;
        _ai = ai;
        _auditTrail = auditTrail;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<RiskSummaryResult> GenerateAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var actor = _currentUser.RequireRole(AppRoles.Checker, AppRoles.Auditor);
        var application = await _applications.GetDetailsAsync(applicationId, cancellationToken)
            ?? throw new NotFoundException("Loan application", applicationId);

        var input = RiskSummaryInput.From(application);
        var result = await _ai.SummariseAsync(input, cancellationToken);

        // The anonymised payload is recorded verbatim so auditors can verify that no personal data was sent.
        await _auditTrail.RecordAsync(AuditEventType.AiRequest, "AiRiskSummaryGenerated", "LoanApplications",
            applicationId.ToString(CultureInfo.InvariantCulture),
            new
            {
                application.ReferenceNumber,
                result.Source,
                result.Model,
                result.IsFallback,
                result.FallbackReason,
                RiskLevel = result.RiskLevel.ToString(),
                InputSent = input,
            },
            cancellationToken);

        _logger.LogInformation("AI risk summary for {Reference} generated for {UserId} via {Source}", application.ReferenceNumber, actor.UserId, result.Source);
        return result;
    }
}
