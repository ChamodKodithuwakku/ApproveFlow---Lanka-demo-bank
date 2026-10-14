using System.ComponentModel.DataAnnotations;
using ApproveFlow.Application.Common.Models;
using ApproveFlow.Application.LoanApplications;
using ApproveFlow.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ApproveFlow.Web.Models;

/// <summary>Query-string filter for the application list and approval queue.</summary>
public sealed class LoanApplicationFilter
{
    public string? Search { get; set; }
    public LoanStatus? Status { get; set; }
    public int? BranchId { get; set; }
    public int? ProductId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public bool Mine { get; set; }
    public string? Sort { get; set; }
    public string? Dir { get; set; }
    public int Page { get; set; } = 1;

    public SortDirection Direction => string.Equals(Dir, "asc", StringComparison.OrdinalIgnoreCase) ? SortDirection.Ascending : SortDirection.Descending;

    public LoanApplicationQuery ToQuery(string? currentUserId, IReadOnlyCollection<LoanStatus>? statuses = null, string? defaultSort = null,
        SortDirection? defaultDirection = null) => new()
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
        Status = Status,
        Statuses = statuses,
        BranchId = BranchId,
        ProductId = ProductId,
        FromDate = From,
        ToDate = To,
        CreatedById = Mine ? currentUserId : null,
        SortBy = Sort ?? defaultSort,
        SortDirection = Sort is null && defaultDirection.HasValue ? defaultDirection.Value : Direction,
        Page = Page,
        PageSize = Paging.DefaultPageSize,
    };

    /// <summary>Route values for paging and sorting links (keeps the other filters).</summary>
    public Dictionary<string, string?> RouteValues(int? page = null, string? sort = null)
    {
        var sortChanged = sort is not null;
        var direction = sortChanged && string.Equals(sort, Sort, StringComparison.OrdinalIgnoreCase) && Direction == SortDirection.Descending
            ? "asc"
            : sortChanged ? "desc" : Dir;

        return new Dictionary<string, string?>
        {
            ["Search"] = Search,
            ["Status"] = Status?.ToString(),
            ["BranchId"] = BranchId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ProductId"] = ProductId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["From"] = From?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["To"] = To?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["Mine"] = Mine ? "true" : null,
            ["Sort"] = sort ?? Sort,
            ["Dir"] = direction,
            ["Page"] = (page ?? (sortChanged ? 1 : Page)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}

public sealed class LoanApplicationListViewModel
{
    public required LoanApplicationFilter Filter { get; init; }
    public required PagedResult<LoanApplicationListItemDto> Result { get; init; }
    public required IReadOnlyList<LookupItem> Branches { get; init; }
    public required IReadOnlyList<ProductLookupItem> Products { get; init; }
    public bool IsQueue { get; init; }
    public decimal? ApprovalLimit { get; init; }
    public string? CurrentUserId { get; init; }
    public DateTime NowUtc { get; init; }
}

public sealed class LoanApplicationFormViewModel
{
    public int? Id { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? RowVersion { get; set; }
    public LoanStatus? Status { get; set; }

    [Required(ErrorMessage = "Select a customer.")]
    [Display(Name = "Customer")]
    public int? CustomerId { get; set; }

    [Required(ErrorMessage = "Select a loan product.")]
    [Display(Name = "Loan product")]
    public int? LoanProductId { get; set; }

    [Required]
    [Range(typeof(decimal), "1", "1000000000", ParseLimitsInInvariantCulture = true)]
    [Display(Name = "Amount (LKR)")]
    public decimal? Amount { get; set; }

    [Required]
    [Range(1, 360)]
    [Display(Name = "Tenure (months)")]
    public int? TenureMonths { get; set; }

    [Required]
    [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true)]
    [Display(Name = "Interest rate (% p.a.)")]
    public decimal? InterestRate { get; set; }

    [Required]
    [StringLength(500, MinimumLength = 10)]
    [Display(Name = "Purpose")]
    public string? Purpose { get; set; }

    [Required]
    [Range(typeof(decimal), "1", "1000000000", ParseLimitsInInvariantCulture = true)]
    [Display(Name = "Gross monthly income (LKR)")]
    public decimal? MonthlyIncome { get; set; }

    [Required]
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true)]
    [Display(Name = "Existing monthly obligations (LKR)")]
    public decimal? ExistingObligations { get; set; }

    [BindNever]
    public IReadOnlyList<CustomerLookupItem> Customers { get; set; } = Array.Empty<CustomerLookupItem>();

    [BindNever]
    public IReadOnlyList<ProductLookupItem> Products { get; set; } = Array.Empty<ProductLookupItem>();

    public bool IsEdit => Id.HasValue;

    public CreateLoanApplicationRequest ToCreateRequest() => new()
    {
        CustomerId = CustomerId ?? 0,
        LoanProductId = LoanProductId ?? 0,
        Amount = Amount ?? 0,
        TenureMonths = TenureMonths ?? 0,
        InterestRate = InterestRate ?? 0,
        Purpose = Purpose ?? string.Empty,
        MonthlyIncome = MonthlyIncome ?? 0,
        ExistingObligations = ExistingObligations ?? 0,
    };

    public UpdateLoanApplicationRequest ToUpdateRequest() => new()
    {
        CustomerId = CustomerId ?? 0,
        LoanProductId = LoanProductId ?? 0,
        Amount = Amount ?? 0,
        TenureMonths = TenureMonths ?? 0,
        InterestRate = InterestRate ?? 0,
        Purpose = Purpose ?? string.Empty,
        MonthlyIncome = MonthlyIncome ?? 0,
        ExistingObligations = ExistingObligations ?? 0,
        RowVersion = RowVersion ?? string.Empty,
    };

    public static LoanApplicationFormViewModel From(LoanApplicationDetailsDto d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new LoanApplicationFormViewModel
        {
            Id = d.Id,
            ReferenceNumber = d.ReferenceNumber,
            RowVersion = d.RowVersion,
            Status = d.Status,
            CustomerId = d.CustomerId,
            LoanProductId = d.LoanProductId,
            Amount = d.Amount,
            TenureMonths = d.TenureMonths,
            InterestRate = d.InterestRate,
            Purpose = d.Purpose,
            MonthlyIncome = d.MonthlyIncome,
            ExistingObligations = d.ExistingObligations,
        };
    }
}

/// <summary>Posted by the approve / reject / return confirmation dialogs.</summary>
public sealed class DecisionInput
{
    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Remarks { get; set; }
}
