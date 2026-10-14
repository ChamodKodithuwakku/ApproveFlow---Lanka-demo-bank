using ApproveFlow.Application.Abstractions.Identity;
using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Application.Common.Models;
using ApproveFlow.Application.LoanApplications;
using ApproveFlow.Application.Lookups;
using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Web.Models;
using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApproveFlow.Web.Controllers;

[Authorize(Policy = Policies.AnyStaff)]
public sealed class LoanApplicationsController : AppController
{
    private static readonly LoanStatus[] PendingStatuses = { LoanStatus.Submitted, LoanStatus.PendingSecondApproval };

    private readonly ILoanApplicationService _applications;
    private readonly ILoanWorkflowService _workflow;
    private readonly ILookupService _lookups;
    private readonly IUserDirectory _users;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public LoanApplicationsController(ILoanApplicationService applications, ILoanWorkflowService workflow, ILookupService lookups,
        IUserDirectory users, ICurrentUser currentUser, TimeProvider clock)
    {
        _applications = applications;
        _workflow = workflow;
        _lookups = lookups;
        _users = users;
        _currentUser = currentUser;
        _clock = clock;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] LoanApplicationFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var result = await _applications.SearchAsync(filter.ToQuery(_currentUser.UserId), cancellationToken);
        return View(await BuildListAsync(filter, result, isQueue: false, cancellationToken));
    }

    /// <summary>Checker work queue: oldest pending items first.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.CheckerOnly)]
    public async Task<IActionResult> Queue([FromQuery] LoanApplicationFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        filter.Status = filter.Status is LoanStatus.Submitted or LoanStatus.PendingSecondApproval ? filter.Status : null;
        var query = filter.ToQuery(_currentUser.UserId, PendingStatuses, LoanApplicationSortFields.Submitted, SortDirection.Ascending);
        var result = await _applications.SearchAsync(query, cancellationToken);
        return View("Index", await BuildListAsync(filter, result, isQueue: true, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        try
        {
            ViewData["NowUtc"] = _clock.GetUtcNow().UtcDateTime;
            return View(await _applications.GetAsync(id, cancellationToken));
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet]
    [Authorize(Policy = Policies.MakerOnly)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View("Form", await WithLookupsAsync(new LoanApplicationFormViewModel(), cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.MakerOnly)]
    public async Task<IActionResult> Create(LoanApplicationFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Id = null;
        if (ModelState.IsValid)
        {
            try
            {
                var created = await _applications.CreateAsync(model.ToCreateRequest(), cancellationToken);
                FlashSuccess($"Draft {created.ReferenceNumber} created. Review it and submit it for approval when ready.");
                return RedirectToAction(nameof(Details), new { id = created.Id });
            }
            catch (ValidationException ex)
            {
                AddErrors(ModelState, ex);
            }
            catch (DomainException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        return View("Form", await WithLookupsAsync(model, cancellationToken));
    }

    [HttpGet]
    [Authorize(Policy = Policies.MakerOnly)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        LoanApplicationDetailsDto details;
        try
        {
            details = await _applications.GetAsync(id, cancellationToken);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        if (!details.Permissions.CanEdit)
        {
            FlashError($"Application {details.ReferenceNumber} cannot be edited: only the maker who created it can edit it while it is a draft or returned for correction.");
            return RedirectToAction(nameof(Details), new { id });
        }

        return View("Form", await WithLookupsAsync(LoanApplicationFormViewModel.From(details), cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.MakerOnly)]
    public async Task<IActionResult> Edit(int id, LoanApplicationFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Id = id;
        if (ModelState.IsValid)
        {
            try
            {
                var updated = await _applications.UpdateAsync(id, model.ToUpdateRequest(), cancellationToken);
                FlashSuccess($"Application {updated.ReferenceNumber} saved.");
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (ValidationException ex)
            {
                AddErrors(ModelState, ex);
            }
            catch (Exception ex) when (IsUserFacing(ex))
            {
                FlashError(ex.Message);
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        return View("Form", await WithLookupsAsync(model, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.MakerOnly)]
    public Task<IActionResult> Submit(int id, DecisionInput input, CancellationToken cancellationToken) =>
        RunAsync(id, async () => (await _workflow.SubmitAsync(id, ToRequest(input), cancellationToken)).Message);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CheckerOnly)]
    public Task<IActionResult> Approve(int id, DecisionInput input, CancellationToken cancellationToken) =>
        RunAsync(id, async () => (await _workflow.ApproveAsync(id, ToRequest(input), cancellationToken)).Message);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CheckerOnly)]
    public Task<IActionResult> Reject(int id, DecisionInput input, CancellationToken cancellationToken) =>
        RunAsync(id, async () => (await _workflow.RejectAsync(id, ToRequest(input), cancellationToken)).Message);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CheckerOnly)]
    public Task<IActionResult> Return(int id, DecisionInput input, CancellationToken cancellationToken) =>
        RunAsync(id, async () => (await _workflow.ReturnForCorrectionAsync(id, ToRequest(input), cancellationToken)).Message);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.MakerOnly)]
    public async Task<IActionResult> Delete(int id, DecisionInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            await _applications.DeleteDraftAsync(id, input.RowVersion, cancellationToken);
            FlashSuccess("The draft application was withdrawn.");
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (IsUserFacing(ex))
        {
            FlashError(ex.Message);
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    /// <summary>AI risk summary (Checker/Auditor). JSON for the details page; rendered client-side with textContent only.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.RiskSummary)]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimitPolicies.Api)]
    public async Task<IActionResult> RiskSummary(int id, [FromServices] ApproveFlow.Application.AiRisk.IRiskSummaryAppService riskSummaries,
        CancellationToken cancellationToken)
    {
        try
        {
            var r = await riskSummaries.GenerateAsync(id, cancellationToken);
            return Json(new
            {
                riskLevel = r.RiskLevel.ToString(),
                summary = r.Summary,
                factors = r.Factors,
                isFallback = r.IsFallback,
                model = r.Model ?? r.Source,
                fallbackReason = r.FallbackReason,
                generatedAt = ApproveFlow.Web.Infrastructure.Display.Time(r.GeneratedAtUtc),
            });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }

    private async Task<IActionResult> RunAsync(int id, Func<Task<string>> action)
    {
        try
        {
            FlashSuccess(await action());
        }
        catch (ValidationException ex)
        {
            FlashError(string.Join(" ", ex.Errors.SelectMany(e => e.Value)));
        }
        catch (Exception ex) when (IsUserFacing(ex))
        {
            FlashError(ex.Message);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private static WorkflowActionRequest ToRequest(DecisionInput? input) =>
        new() { RowVersion = input?.RowVersion ?? string.Empty, Remarks = input?.Remarks };

    private async Task<LoanApplicationListViewModel> BuildListAsync(LoanApplicationFilter filter, PagedResult<LoanApplicationListItemDto> result,
        bool isQueue, CancellationToken cancellationToken)
    {
        decimal? limit = null;
        if (isQueue && _currentUser.UserId is { } userId)
        {
            limit = (await _users.GetAsync(userId, cancellationToken))?.ApprovalLimit ?? 0m;
        }

        return new LoanApplicationListViewModel
        {
            Filter = filter,
            Result = result,
            Branches = await _lookups.GetBranchesAsync(cancellationToken),
            Products = await _lookups.GetProductsAsync(activeOnly: false, cancellationToken),
            IsQueue = isQueue,
            ApprovalLimit = limit,
            CurrentUserId = _currentUser.UserId,
            NowUtc = _clock.GetUtcNow().UtcDateTime,
        };
    }

    private async Task<LoanApplicationFormViewModel> WithLookupsAsync(LoanApplicationFormViewModel model, CancellationToken cancellationToken)
    {
        model.Customers = await _lookups.GetCustomersAsync(cancellationToken);
        model.Products = await _lookups.GetProductsAsync(activeOnly: true, cancellationToken);
        return model;
    }
}
