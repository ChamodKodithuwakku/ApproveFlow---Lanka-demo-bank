using System.Globalization;
using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Domain.Rules;

namespace ApproveFlow.Domain.Entities;

/// <summary>
/// Aggregate root for the maker-checker workflow. All state transitions and the rules that guard them
/// (ownership, four-eyes, approval limits, dual approval, mandatory remarks, immutability) live here so that
/// no caller — UI, API or import — can bypass them.
/// </summary>
public class LoanApplication : AuditableEntity, ISoftDeletable
{
    private readonly List<ApprovalAction> _actions = new();

    private LoanApplication()
    {
    }

    public string ReferenceNumber { get; private set; } = string.Empty;
    public int CustomerId { get; private set; }
    public Customer? Customer { get; private set; }
    public int LoanProductId { get; private set; }
    public LoanProduct? LoanProduct { get; private set; }
    public int BranchId { get; private set; }
    public Branch? Branch { get; private set; }

    public decimal Amount { get; private set; }
    public int TenureMonths { get; private set; }

    /// <summary>Annual interest rate in percent.</summary>
    public decimal InterestRate { get; private set; }

    public string Purpose { get; private set; } = string.Empty;
    public decimal MonthlyIncome { get; private set; }
    public decimal ExistingObligations { get; private set; }

    /// <summary>Calculated equated monthly instalment (EMI) for the requested terms.</summary>
    public decimal MonthlyInstallment { get; private set; }

    /// <summary>Calculated debt-burden ratio in percent: (obligations + EMI) / income × 100.</summary>
    public decimal DebtBurdenRatio { get; private set; }

    public LoanStatus Status { get; private set; }

    /// <summary>The maker who last changed the application's content. Used by the four-eyes rule.</summary>
    public string LastEditedById { get; private set; } = string.Empty;

    public string LastEditedByName { get; private set; } = string.Empty;

    public DateTime? SubmittedAtUtc { get; private set; }
    public string? SubmittedById { get; private set; }

    public string? FirstApproverId { get; private set; }
    public string? FirstApproverName { get; private set; }
    public DateTime? FirstApprovedAtUtc { get; private set; }

    public string? DecidedById { get; private set; }
    public string? DecidedByName { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }

    public bool IsImported { get; private set; }
    public int? ImportBatchId { get; private set; }

    /// <summary>Optimistic concurrency token (SQL Server rowversion).</summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public string? DeletedById { get; private set; }

    public IReadOnlyCollection<ApprovalAction> Actions => _actions.AsReadOnly();

    public bool IsFinal => Status is LoanStatus.Approved or LoanStatus.Rejected;

    public bool IsPendingDecision => Status is LoanStatus.Submitted or LoanStatus.PendingSecondApproval;

    public bool IsEditable => Status is LoanStatus.Draft or LoanStatus.ReturnedForCorrection;

    public static LoanApplication CreateDraft(string referenceNumber, Customer customer, LoanProduct product, Branch branch,
        LoanTerms terms, Actor maker, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(maker);

        var application = new LoanApplication
        {
            ReferenceNumber = Guard.Required(referenceNumber, nameof(referenceNumber)),
            Status = LoanStatus.Draft,
        };

        application.SetParties(customer, product, branch, terms);
        application.MarkEditedBy(maker);
        application.StampCreated(maker.UserId, maker.UserName, nowUtc);
        application.AddAction(ApprovalActionType.Created, null, LoanStatus.Draft, maker, null, nowUtc);
        return application;
    }

    /// <summary>Creates an application migrated from the legacy system in its historical status.</summary>
    public static LoanApplication CreateFromLegacy(string legacyReference, Customer customer, LoanProduct product, Branch branch,
        LoanTerms terms, LoanStatus legacyStatus, DateTime applicationDateUtc, int importBatchId, Actor importer, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(importer);
        if (legacyStatus is not (LoanStatus.Submitted or LoanStatus.Approved or LoanStatus.Rejected))
        {
            throw new BusinessRuleViolationException("legacy_status", "Legacy applications can only be imported as Submitted, Approved or Rejected.");
        }

        var application = new LoanApplication
        {
            ReferenceNumber = Guard.Required(legacyReference, nameof(legacyReference)).ToUpperInvariant(),
            Status = legacyStatus,
            IsImported = true,
            ImportBatchId = importBatchId,
            SubmittedAtUtc = applicationDateUtc,
            SubmittedById = importer.UserId,
        };

        application.SetParties(customer, product, branch, terms);
        application.MarkEditedBy(importer);
        application.StampCreated(importer.UserId, importer.UserName, applicationDateUtc);
        application.AddAction(ApprovalActionType.Imported, null, legacyStatus, importer,
            string.Create(CultureInfo.InvariantCulture, $"Migrated from legacy system in import batch #{importBatchId}."), nowUtc);
        return application;
    }

    /// <summary>Maker edits a Draft or Returned application they own.</summary>
    public void UpdateDetails(Customer customer, LoanProduct product, Branch branch, LoanTerms terms, Actor maker, DateTime nowUtc)
    {
        EnsureNotDeleted();
        EnsureEditable();
        EnsureOwner(maker, "edit");
        SetParties(customer, product, branch, terms);
        MarkEditedBy(maker);
        StampUpdated(maker.UserId, maker.UserName, nowUtc);
    }

    public void Submit(Actor maker, DateTime nowUtc)
    {
        EnsureNotDeleted();
        if (!IsEditable)
        {
            throw InvalidTransition("submit");
        }

        EnsureOwner(maker, "submit");

        var from = Status;
        Status = LoanStatus.Submitted;
        SubmittedAtUtc = nowUtc;
        SubmittedById = maker.UserId;
        ClearDecisionState();
        MarkEditedBy(maker);
        StampUpdated(maker.UserId, maker.UserName, nowUtc);
        AddAction(from == LoanStatus.Draft ? ApprovalActionType.Submitted : ApprovalActionType.Resubmitted, from, Status, maker, null, nowUtc);
    }

    /// <summary>
    /// Checker approval. If the amount is within the checker's limit the application is approved; otherwise the
    /// approval is recorded as the first of two and a different checker must give the second approval.
    /// </summary>
    public ApprovalOutcome Approve(Actor checker, decimal checkerApprovalLimit, string? remarks, DateTime nowUtc)
    {
        if (checkerApprovalLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(checkerApprovalLimit), "Approval limit cannot be negative.");
        }

        EnsureDecisionAllowed(checker, "approve");
        EnsureRemarksLength(remarks, required: false);

        var from = Status;
        if (Status == LoanStatus.Submitted && Amount > checkerApprovalLimit)
        {
            Status = LoanStatus.PendingSecondApproval;
            FirstApproverId = checker.UserId;
            FirstApproverName = checker.UserName;
            FirstApprovedAtUtc = nowUtc;
            StampUpdated(checker.UserId, checker.UserName, nowUtc);
            AddAction(ApprovalActionType.FirstApproval, from, Status, checker,
                remarks ?? string.Create(CultureInfo.InvariantCulture,
                    $"Amount exceeds approver limit (LKR {checkerApprovalLimit:N2}); second approval required."), nowUtc);
            return ApprovalOutcome.PendingSecondApproval;
        }

        Status = LoanStatus.Approved;
        RecordDecision(checker, nowUtc);
        AddAction(ApprovalActionType.Approved, from, Status, checker, remarks, nowUtc);
        return ApprovalOutcome.Approved;
    }

    public void Reject(Actor checker, string? remarks, DateTime nowUtc)
    {
        EnsureDecisionAllowed(checker, "reject");
        EnsureRemarksLength(remarks, required: true);

        var from = Status;
        Status = LoanStatus.Rejected;
        RecordDecision(checker, nowUtc);
        AddAction(ApprovalActionType.Rejected, from, Status, checker, remarks, nowUtc);
    }

    public void ReturnForCorrection(Actor checker, string? remarks, DateTime nowUtc)
    {
        EnsureDecisionAllowed(checker, "return");
        EnsureRemarksLength(remarks, required: true);

        var from = Status;
        Status = LoanStatus.ReturnedForCorrection;
        ClearDecisionState();
        StampUpdated(checker.UserId, checker.UserName, nowUtc);
        AddAction(ApprovalActionType.ReturnedForCorrection, from, Status, checker, remarks, nowUtc);
    }

    /// <summary>Only an unsubmitted draft can be withdrawn; it is soft-deleted, never physically removed.</summary>
    public void SoftDelete(Actor maker, DateTime nowUtc)
    {
        EnsureNotDeleted();
        if (Status != LoanStatus.Draft)
        {
            throw InvalidTransition("delete");
        }

        EnsureOwner(maker, "delete");
        IsDeleted = true;
        DeletedAtUtc = nowUtc;
        DeletedById = maker.UserId;
        StampUpdated(maker.UserId, maker.UserName, nowUtc);
    }

    /// <summary>True when the given user is barred from deciding on this application by the four-eyes rule.</summary>
    public bool IsMakerOf(string userId) =>
        string.Equals(CreatedById, userId, StringComparison.Ordinal)
        || string.Equals(LastEditedById, userId, StringComparison.Ordinal)
        || string.Equals(SubmittedById, userId, StringComparison.Ordinal);

    private void SetParties(Customer customer, LoanProduct product, Branch branch, LoanTerms terms)
    {
        if (!customer.IsActive)
        {
            throw new BusinessRuleViolationException("customer_inactive", "The selected customer is inactive.");
        }

        if (!product.IsActive)
        {
            throw new BusinessRuleViolationException("product_inactive", "The selected loan product is not currently offered.");
        }

        if (!branch.IsActive)
        {
            throw new BusinessRuleViolationException("branch_inactive", "The selected branch is inactive.");
        }

        product.EnsureWithinLimits(terms.Amount, terms.TenureMonths);

        CustomerId = customer.Id;
        LoanProductId = product.Id;
        BranchId = branch.Id;
        Amount = terms.Amount;
        TenureMonths = terms.TenureMonths;
        InterestRate = terms.InterestRate;
        Purpose = terms.Purpose;
        MonthlyIncome = terms.MonthlyIncome;
        ExistingObligations = terms.ExistingObligations;
        MonthlyInstallment = terms.MonthlyInstallment;
        DebtBurdenRatio = terms.DebtBurdenRatio;
    }

    private void EnsureDecisionAllowed(Actor checker, string verb)
    {
        ArgumentNullException.ThrowIfNull(checker);
        EnsureNotDeleted();

        if (!IsPendingDecision)
        {
            throw InvalidTransition(verb);
        }

        // Four-eyes principle: whoever created, last edited or submitted the application can never decide on it.
        if (IsMakerOf(checker.UserId))
        {
            throw new SegregationOfDutiesException("four_eyes_violation",
                "Four-eyes principle: you created or last modified this application, so you cannot approve, reject or return it.");
        }

        // Dual approval: the second decision must come from a different checker than the first approver.
        if (Status == LoanStatus.PendingSecondApproval && checker.Is(FirstApproverId))
        {
            throw new SegregationOfDutiesException("dual_approval_same_checker",
                "You gave the first approval; a different checker must complete the second approval.");
        }
    }

    private static void EnsureRemarksLength(string? remarks, bool required)
    {
        var length = remarks?.Trim().Length ?? 0;
        if (required && length < LoanPolicy.MinRemarksLength)
        {
            throw new BusinessRuleViolationException("remarks_required",
                $"Remarks are mandatory and must be at least {LoanPolicy.MinRemarksLength} characters.");
        }

        if (length > LoanPolicy.MaxRemarksLength)
        {
            throw new BusinessRuleViolationException("remarks_too_long",
                $"Remarks cannot exceed {LoanPolicy.MaxRemarksLength} characters.");
        }
    }

    private void EnsureEditable()
    {
        if (IsFinal)
        {
            throw new InvalidStatusTransitionException("immutable_record",
                $"Application {ReferenceNumber} is {Status} and can no longer be modified.");
        }

        if (!IsEditable)
        {
            throw InvalidTransition("edit");
        }
    }

    private void EnsureOwner(Actor maker, string verb)
    {
        ArgumentNullException.ThrowIfNull(maker);
        if (!maker.Is(CreatedById))
        {
            throw new SegregationOfDutiesException("not_owner", $"Only the maker who created application {ReferenceNumber} can {verb} it.");
        }
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new InvalidStatusTransitionException("deleted", $"Application {ReferenceNumber} has been deleted.");
        }
    }

    private InvalidStatusTransitionException InvalidTransition(string verb) =>
        IsFinal
            ? new InvalidStatusTransitionException("immutable_record", $"Application {ReferenceNumber} is {Status} and can no longer be modified.")
            : new InvalidStatusTransitionException("invalid_status_transition", $"Cannot {verb} application {ReferenceNumber} while it is {Status}.");

    private void MarkEditedBy(Actor maker)
    {
        LastEditedById = maker.UserId;
        LastEditedByName = maker.UserName;
    }

    private void RecordDecision(Actor checker, DateTime nowUtc)
    {
        DecidedById = checker.UserId;
        DecidedByName = checker.UserName;
        DecidedAtUtc = nowUtc;
        StampUpdated(checker.UserId, checker.UserName, nowUtc);
    }

    private void ClearDecisionState()
    {
        FirstApproverId = null;
        FirstApproverName = null;
        FirstApprovedAtUtc = null;
        DecidedById = null;
        DecidedByName = null;
        DecidedAtUtc = null;
    }

    private void AddAction(ApprovalActionType type, LoanStatus? from, LoanStatus to, Actor actor, string? remarks, DateTime atUtc) =>
        _actions.Add(new ApprovalAction(this, type, from, to, actor, remarks, atUtc));
}
