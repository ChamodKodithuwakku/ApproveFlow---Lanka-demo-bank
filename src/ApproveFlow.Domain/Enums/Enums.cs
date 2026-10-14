namespace ApproveFlow.Domain.Enums;

/// <summary>
/// Loan application lifecycle.
/// Draft → Submitted → (Approved | Rejected | ReturnedForCorrection); ReturnedForCorrection → Submitted.
/// PendingSecondApproval is the intermediate state used for dual approval when the amount exceeds the
/// first checker's approval limit.
/// </summary>
public enum LoanStatus
{
    Draft = 0,
    Submitted = 1,
    PendingSecondApproval = 2,
    Approved = 3,
    Rejected = 4,
    ReturnedForCorrection = 5,
}

/// <summary>Type of an entry in the approval history (one row per state change).</summary>
public enum ApprovalActionType
{
    Created = 0,
    Submitted = 1,
    Resubmitted = 2,
    FirstApproval = 3,
    Approved = 4,
    Rejected = 5,
    ReturnedForCorrection = 6,
    Imported = 7,
}

/// <summary>Result of an approve action.</summary>
public enum ApprovalOutcome
{
    /// <summary>The application is now fully approved.</summary>
    Approved = 0,

    /// <summary>The amount exceeded the checker's limit; a second checker must approve.</summary>
    PendingSecondApproval = 1,
}

public enum AuditEventType
{
    EntityChange = 0,
    Security = 1,
    AiRequest = 2,
}

public enum ImportBatchStatus
{
    Completed = 0,
    Failed = 1,
}

public enum RiskLevel
{
    Low = 0,
    Moderate = 1,
    High = 2,
}
