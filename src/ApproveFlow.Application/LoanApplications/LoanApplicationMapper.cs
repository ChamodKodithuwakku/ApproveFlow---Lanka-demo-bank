using ApproveFlow.Application.Common.Validation;
using ApproveFlow.Domain.Entities;

namespace ApproveFlow.Application.LoanApplications;

internal static class LoanApplicationMapper
{
    public static LoanApplicationDetailsDto ToDetails(LoanApplication a, LoanApplicationPermissions permissions) => new()
    {
        Id = a.Id,
        ReferenceNumber = a.ReferenceNumber,
        Status = a.Status,
        CustomerId = a.CustomerId,
        CustomerNumber = a.Customer?.CustomerNumber ?? string.Empty,
        CustomerName = a.Customer?.FullName ?? string.Empty,
        CustomerNic = a.Customer?.Nic ?? string.Empty,
        BranchId = a.BranchId,
        BranchName = a.Branch?.Name ?? string.Empty,
        LoanProductId = a.LoanProductId,
        ProductCode = a.LoanProduct?.Code ?? string.Empty,
        ProductName = a.LoanProduct?.Name ?? string.Empty,
        Amount = a.Amount,
        TenureMonths = a.TenureMonths,
        InterestRate = a.InterestRate,
        Purpose = a.Purpose,
        MonthlyIncome = a.MonthlyIncome,
        ExistingObligations = a.ExistingObligations,
        MonthlyInstallment = a.MonthlyInstallment,
        DebtBurdenRatio = a.DebtBurdenRatio,
        CreatedById = a.CreatedById,
        CreatedByName = a.CreatedByName,
        CreatedAtUtc = a.CreatedAtUtc,
        UpdatedByName = a.UpdatedByName,
        UpdatedAtUtc = a.UpdatedAtUtc,
        LastEditedByName = a.LastEditedByName,
        SubmittedAtUtc = a.SubmittedAtUtc,
        FirstApproverName = a.FirstApproverName,
        FirstApprovedAtUtc = a.FirstApprovedAtUtc,
        DecidedByName = a.DecidedByName,
        DecidedAtUtc = a.DecidedAtUtc,
        IsImported = a.IsImported,
        RowVersion = RowVersions.Encode(a.RowVersion),
        History = a.Actions.OrderBy(x => x.PerformedAtUtc).ThenBy(x => x.Id).Select(ToDto).ToList(),
        Permissions = permissions,
    };

    public static ApprovalActionDto ToDto(ApprovalAction a) =>
        new(a.Id, a.ActionType, a.FromStatus, a.ToStatus, a.PerformedById, a.PerformedByName, a.Remarks, a.PerformedAtUtc);
}
