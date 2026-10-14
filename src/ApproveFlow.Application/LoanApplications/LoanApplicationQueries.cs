using ApproveFlow.Application.Common.Models;
using ApproveFlow.Domain.Enums;

namespace ApproveFlow.Application.LoanApplications;

/// <summary>Search/filter/sort/paging criteria for loan applications (shared by UI and API).</summary>
public sealed record LoanApplicationQuery
{
    /// <summary>Free text: matches reference number, customer name or NIC.</summary>
    public string? Search { get; init; }

    public LoanStatus? Status { get; init; }

    /// <summary>Optional set of statuses (used by the approval queue). Ignored when <see cref="Status"/> is set.</summary>
    public IReadOnlyCollection<LoanStatus>? Statuses { get; init; }

    public int? BranchId { get; init; }
    public int? ProductId { get; init; }

    /// <summary>Inclusive start date (Asia/Colombo) applied to the created date.</summary>
    public DateOnly? FromDate { get; init; }

    /// <summary>Inclusive end date (Asia/Colombo) applied to the created date.</summary>
    public DateOnly? ToDate { get; init; }

    public string? CreatedById { get; init; }

    /// <summary>One of <see cref="LoanApplicationSortFields"/>; anything else falls back to created date.</summary>
    public string? SortBy { get; init; }

    public SortDirection SortDirection { get; init; } = SortDirection.Descending;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}

/// <summary>White-listed sort keys. User input is never concatenated into a query.</summary>
public static class LoanApplicationSortFields
{
    public const string Reference = "reference";
    public const string Customer = "customer";
    public const string Amount = "amount";
    public const string Status = "status";
    public const string Created = "created";
    public const string Submitted = "submitted";

    public static readonly IReadOnlyList<string> All = new[] { Reference, Customer, Amount, Status, Created, Submitted };
}

public sealed record LoanApplicationListItemDto(
    int Id,
    string ReferenceNumber,
    string CustomerName,
    string BranchName,
    string ProductName,
    decimal Amount,
    int TenureMonths,
    decimal DebtBurdenRatio,
    LoanStatus Status,
    string CreatedById,
    string CreatedByName,
    DateTime CreatedAtUtc,
    DateTime? SubmittedAtUtc,
    string? FirstApproverId,
    bool IsImported);
