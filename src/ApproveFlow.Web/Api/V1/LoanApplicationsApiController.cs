using System.ComponentModel.DataAnnotations;
using ApproveFlow.Application.Common.Models;
using ApproveFlow.Application.LoanApplications;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApproveFlow.Web.Api.V1;

/// <summary>Loan applications and the maker-checker workflow.</summary>
[Route("api/v1/loan-applications")]
[Tags("Loan applications")]
[Authorize(Policy = Policies.AnyStaff)]
public sealed class LoanApplicationsApiController : ApiControllerBase
{
    private readonly ILoanApplicationService _applications;
    private readonly ILoanWorkflowService _workflow;

    public LoanApplicationsApiController(ILoanApplicationService applications, ILoanWorkflowService workflow)
    {
        _applications = applications;
        _workflow = workflow;
    }

    /// <summary>Searches loan applications with filtering, sorting and paging.</summary>
    /// <response code="200">A page of applications.</response>
    /// <response code="400">Invalid query parameters (e.g. pageSize above 100 or unknown sortBy).</response>
    /// <response code="403">The caller has no staff role.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<LoanApplicationListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<PagedResult<LoanApplicationListItemDto>> List([FromQuery] LoanApplicationListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _applications.SearchAsync(new LoanApplicationQuery
        {
            Search = query.Search,
            Status = query.Status,
            BranchId = query.BranchId,
            ProductId = query.ProductId,
            FromDate = query.FromDate,
            ToDate = query.ToDate,
            SortBy = query.SortBy,
            SortDirection = string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? SortDirection.Ascending : SortDirection.Descending,
            Page = query.Page,
            PageSize = query.PageSize,
        }, cancellationToken);
    }

    /// <summary>Gets one application, including its approval history, RowVersion and what the caller may do with it.</summary>
    /// <response code="200">The application.</response>
    /// <response code="404">No application with this id.</response>
    [HttpGet("{id:int}", Name = "GetLoanApplication")]
    [ProducesResponseType(typeof(LoanApplicationDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<LoanApplicationDetailsDto> Get(int id, CancellationToken cancellationToken) => _applications.GetAsync(id, cancellationToken);

    /// <summary>Creates a draft application (Maker).</summary>
    /// <remarks>The reference number (LDB-YYYY-NNNNNN), EMI and debt-burden ratio are calculated by the server.</remarks>
    /// <response code="201">Draft created; Location header points to the new resource.</response>
    /// <response code="403">Caller is not a Maker.</response>
    /// <response code="422">Validation or business-rule failure (e.g. amount outside product range).</response>
    [HttpPost]
    [Authorize(Policy = Policies.MakerOnly)]
    [ProducesResponseType(typeof(LoanApplicationDetailsDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LoanApplicationDetailsDto>> Create([FromBody] CreateLoanApplicationRequest request, CancellationToken cancellationToken)
    {
        var created = await _applications.CreateAsync(request, cancellationToken);
        return CreatedAtRoute("GetLoanApplication", new { id = created.Id }, created);
    }

    /// <summary>Updates a Draft or Returned application owned by the calling Maker.</summary>
    /// <response code="200">Updated application.</response>
    /// <response code="403">Caller is not the owning Maker.</response>
    /// <response code="404">No application with this id.</response>
    /// <response code="409">RowVersion is stale, or the application is no longer editable (e.g. Approved).</response>
    /// <response code="422">Validation or business-rule failure.</response>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.MakerOnly)]
    [ProducesResponseType(typeof(LoanApplicationDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<LoanApplicationDetailsDto> Update(int id, [FromBody] UpdateLoanApplicationRequest request, CancellationToken cancellationToken) =>
        _applications.UpdateAsync(id, request, cancellationToken);

    /// <summary>Submits a Draft (or resubmits a Returned) application for approval (owning Maker).</summary>
    /// <response code="200">New status and RowVersion.</response>
    /// <response code="403">Caller is not the owning Maker.</response>
    /// <response code="409">Stale RowVersion or invalid state.</response>
    [HttpPost("{id:int}/submit")]
    [Authorize(Policy = Policies.MakerOnly)]
    [ProducesResponseType(typeof(WorkflowResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<WorkflowResult> Submit(int id, [FromBody] WorkflowActionRequest request, CancellationToken cancellationToken) =>
        _workflow.SubmitAsync(id, request, cancellationToken);

    /// <summary>Approves an application (Checker).</summary>
    /// <remarks>
    /// If the amount exceeds the caller's approval limit the approval is recorded as the first of two and the status
    /// becomes <c>PendingSecondApproval</c>; a different checker must approve again. The maker can never approve.
    /// </remarks>
    /// <response code="200">New status (Approved or PendingSecondApproval) and RowVersion.</response>
    /// <response code="403">Not a Checker, or four-eyes / dual-approval rule violated.</response>
    /// <response code="409">Stale RowVersion or the application is not awaiting a decision.</response>
    [HttpPost("{id:int}/approve")]
    [Authorize(Policy = Policies.CheckerOnly)]
    [ProducesResponseType(typeof(WorkflowResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<WorkflowResult> Approve(int id, [FromBody] WorkflowActionRequest request, CancellationToken cancellationToken) =>
        _workflow.ApproveAsync(id, request, cancellationToken);

    /// <summary>Rejects an application (Checker). Remarks of at least 10 characters are mandatory.</summary>
    /// <response code="200">Status Rejected and new RowVersion.</response>
    /// <response code="403">Not a Checker, or four-eyes rule violated.</response>
    /// <response code="409">Stale RowVersion or invalid state.</response>
    /// <response code="422">Remarks missing or shorter than 10 characters.</response>
    [HttpPost("{id:int}/reject")]
    [Authorize(Policy = Policies.CheckerOnly)]
    [ProducesResponseType(typeof(WorkflowResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<WorkflowResult> Reject(int id, [FromBody] WorkflowActionRequest request, CancellationToken cancellationToken) =>
        _workflow.RejectAsync(id, request, cancellationToken);

    /// <summary>Returns an application to its maker for correction (Checker). Remarks of at least 10 characters are mandatory.</summary>
    /// <response code="200">Status ReturnedForCorrection and new RowVersion.</response>
    /// <response code="403">Not a Checker, or four-eyes rule violated.</response>
    /// <response code="409">Stale RowVersion or invalid state.</response>
    /// <response code="422">Remarks missing or shorter than 10 characters.</response>
    [HttpPost("{id:int}/return")]
    [Authorize(Policy = Policies.CheckerOnly)]
    [ProducesResponseType(typeof(WorkflowResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<WorkflowResult> Return(int id, [FromBody] WorkflowActionRequest request, CancellationToken cancellationToken) =>
        _workflow.ReturnForCorrectionAsync(id, request, cancellationToken);

    /// <summary>Gets the approval history (one entry per state change: who, action, remarks, timestamp).</summary>
    /// <response code="200">History in chronological order.</response>
    /// <response code="404">No application with this id.</response>
    [HttpGet("{id:int}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<ApprovalActionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<ApprovalActionDto>> History(int id, CancellationToken cancellationToken) =>
        _applications.GetHistoryAsync(id, cancellationToken);
}

/// <summary>Query parameters for <c>GET /api/v1/loan-applications</c>.</summary>
public sealed record LoanApplicationListQuery
{
    /// <summary>Free text matched against reference number, customer name and NIC.</summary>
    [StringLength(100)]
    public string? Search { get; init; }

    /// <summary>Filter by status.</summary>
    public LoanStatus? Status { get; init; }

    /// <summary>Filter by branch id.</summary>
    [Range(1, int.MaxValue)]
    public int? BranchId { get; init; }

    /// <summary>Filter by loan product id.</summary>
    [Range(1, int.MaxValue)]
    public int? ProductId { get; init; }

    /// <summary>Created on or after this date (Asia/Colombo), format yyyy-MM-dd.</summary>
    public DateOnly? FromDate { get; init; }

    /// <summary>Created on or before this date (Asia/Colombo), format yyyy-MM-dd.</summary>
    public DateOnly? ToDate { get; init; }

    /// <summary>One of: reference, customer, amount, status, created, submitted. Default: created.</summary>
    [RegularExpression("^(reference|customer|amount|status|created|submitted)$", ErrorMessage = "sortBy must be reference, customer, amount, status, created or submitted.")]
    public string? SortBy { get; init; }

    /// <summary>asc or desc (default desc).</summary>
    [RegularExpression("^(asc|desc)$", ErrorMessage = "sortDirection must be asc or desc.")]
    public string? SortDirection { get; init; }

    /// <summary>1-based page number.</summary>
    [Range(1, 100_000)]
    public int Page { get; init; } = 1;

    /// <summary>Page size, 1-100 (default 20).</summary>
    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
