using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Enums;

namespace ApproveFlow.Domain.Entities;

/// <summary>Immutable history entry written for every state change of a loan application.</summary>
public class ApprovalAction : Entity
{
    private ApprovalAction()
    {
    }

    internal ApprovalAction(LoanApplication application, ApprovalActionType actionType, LoanStatus? fromStatus,
        LoanStatus toStatus, Actor actor, string? remarks, DateTime performedAtUtc)
    {
        LoanApplication = application;
        ActionType = actionType;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        PerformedById = actor.UserId;
        PerformedByName = actor.UserName;
        Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
        PerformedAtUtc = performedAtUtc;
    }

    public int LoanApplicationId { get; private set; }
    public LoanApplication? LoanApplication { get; private set; }
    public ApprovalActionType ActionType { get; private set; }
    public LoanStatus? FromStatus { get; private set; }
    public LoanStatus ToStatus { get; private set; }
    public string PerformedById { get; private set; } = string.Empty;
    public string PerformedByName { get; private set; } = string.Empty;
    public string? Remarks { get; private set; }
    public DateTime PerformedAtUtc { get; private set; }
}
