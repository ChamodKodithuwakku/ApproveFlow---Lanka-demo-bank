using ApproveFlow.Domain.Enums;

namespace ApproveFlow.Application.LoanApplications;

/// <summary>Fields a maker supplies when creating a loan application.</summary>
public record CreateLoanApplicationRequest
{
    /// <summary>Existing customer id.</summary>
    public int CustomerId { get; init; }

    /// <summary>Loan product id (amount and tenure must be within the product's limits).</summary>
    public int LoanProductId { get; init; }

    /// <summary>Requested amount in LKR.</summary>
    public decimal Amount { get; init; }

    /// <summary>Tenure in months (1-360, and within the product range).</summary>
    public int TenureMonths { get; init; }

    /// <summary>Annual interest rate in percent.</summary>
    public decimal InterestRate { get; init; }

    /// <summary>Purpose of the loan (10-500 characters).</summary>
    public string Purpose { get; init; } = string.Empty;

    /// <summary>Applicant's verified gross monthly income in LKR.</summary>
    public decimal MonthlyIncome { get; init; }

    /// <summary>Existing monthly loan repayments in LKR.</summary>
    public decimal ExistingObligations { get; init; }
}

/// <summary>Edit of a Draft or Returned application. Requires the RowVersion the client last read.</summary>
public sealed record UpdateLoanApplicationRequest : CreateLoanApplicationRequest
{
    /// <summary>Base64 concurrency token from the last GET. A mismatch returns 409 Conflict.</summary>
    public string RowVersion { get; init; } = string.Empty;
}

/// <summary>Submit / approve / reject / return request.</summary>
public sealed record WorkflowActionRequest
{
    /// <summary>Mandatory (min 10 characters) for reject and return; optional for approve and submit.</summary>
    public string? Remarks { get; init; }

    /// <summary>Base64 concurrency token from the last GET. A mismatch returns 409 Conflict.</summary>
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record WorkflowResult(int Id, string ReferenceNumber, LoanStatus Status, string Message, string RowVersion);

public sealed record ApprovalActionDto(
    int Id,
    ApprovalActionType ActionType,
    LoanStatus? FromStatus,
    LoanStatus ToStatus,
    string PerformedById,
    string PerformedByName,
    string? Remarks,
    DateTime PerformedAtUtc);

/// <summary>What the current user may do with an application (drives buttons in the UI; always re-checked server-side).</summary>
public sealed record LoanApplicationPermissions(
    bool CanEdit,
    bool CanSubmit,
    bool CanDelete,
    bool CanDecide,
    bool CanGenerateRiskSummary,
    string? DecisionBlockedReason,
    bool ApprovalNeedsSecondChecker);

public sealed record LoanApplicationDetailsDto
{
    public int Id { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public LoanStatus Status { get; init; }
    public int CustomerId { get; init; }
    public string CustomerNumber { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerNic { get; init; } = string.Empty;
    public int BranchId { get; init; }
    public string BranchName { get; init; } = string.Empty;
    public int LoanProductId { get; init; }
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public int TenureMonths { get; init; }
    public decimal InterestRate { get; init; }
    public string Purpose { get; init; } = string.Empty;
    public decimal MonthlyIncome { get; init; }
    public decimal ExistingObligations { get; init; }
    public decimal MonthlyInstallment { get; init; }
    public decimal DebtBurdenRatio { get; init; }
    public string CreatedById { get; init; } = string.Empty;
    public string CreatedByName { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public string? UpdatedByName { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
    public string LastEditedByName { get; init; } = string.Empty;
    public DateTime? SubmittedAtUtc { get; init; }
    public string? FirstApproverName { get; init; }
    public DateTime? FirstApprovedAtUtc { get; init; }
    public string? DecidedByName { get; init; }
    public DateTime? DecidedAtUtc { get; init; }
    public bool IsImported { get; init; }

    /// <summary>Base64 optimistic-concurrency token; send it back with any change.</summary>
    public string RowVersion { get; init; } = string.Empty;

    public IReadOnlyList<ApprovalActionDto> History { get; init; } = Array.Empty<ApprovalActionDto>();
    public LoanApplicationPermissions Permissions { get; init; } = new(false, false, false, false, false, null, false);
}

public sealed record LookupItem(int Id, string Code, string Name);

public sealed record ProductLookupItem(int Id, string Code, string Name, decimal MinAmount, decimal MaxAmount,
    int MinTenureMonths, int MaxTenureMonths, decimal DefaultInterestRate);

public sealed record CustomerLookupItem(int Id, string CustomerNumber, string FullName, string Nic, int BranchId, string BranchName);
