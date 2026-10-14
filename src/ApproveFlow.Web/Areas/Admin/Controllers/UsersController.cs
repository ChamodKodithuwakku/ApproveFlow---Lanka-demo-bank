using System.ComponentModel.DataAnnotations;
using ApproveFlow.Application.Administration;
using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Application.LoanApplications;
using ApproveFlow.Application.Lookups;
using ApproveFlow.Web.Controllers;
using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ValidationException = ApproveFlow.Application.Common.Exceptions.ValidationException;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ApproveFlow.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class UsersController : AppController
{
    private readonly IUserAdministrationService _users;
    private readonly ILookupService _lookups;

    public UsersController(IUserAdministrationService users, ILookupService lookups)
    {
        _users = users;
        _lookups = lookups;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(await _users.GetUsersAsync(cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View("Form", await WithBranchesAsync(new UserFormViewModel(), cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.TemporaryPassword))
        {
            ModelState.AddModelError(nameof(model.TemporaryPassword), "A temporary password is required.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                await _users.CreateUserAsync(new CreateUserRequest
                {
                    Email = model.Email ?? string.Empty,
                    FullName = model.FullName ?? string.Empty,
                    BranchId = model.BranchId ?? 0,
                    Roles = model.Roles,
                    ApprovalLimit = model.ApprovalLimit,
                    TemporaryPassword = model.TemporaryPassword ?? string.Empty,
                }, cancellationToken);
                FlashSuccess($"User {model.Email} created. Ask them to change the temporary password after first sign-in.");
                return RedirectToAction(nameof(Index));
            }
            catch (ValidationException ex)
            {
                AddErrors(ModelState, ex);
            }
        }

        return View("Form", await WithBranchesAsync(model, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _users.GetUserAsync(id, cancellationToken);
            return View("Form", await WithBranchesAsync(new UserFormViewModel
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                BranchId = user.BranchId,
                Roles = user.Roles.ToList(),
                ApprovalLimit = user.ApprovalLimit,
                IsActive = user.IsActive,
                ConcurrencyStamp = user.ConcurrencyStamp,
            }, cancellationToken));
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, UserFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Id = id;
        ModelState.Remove(nameof(model.TemporaryPassword));
        if (ModelState.IsValid)
        {
            try
            {
                await _users.UpdateUserAsync(new UpdateUserRequest
                {
                    Id = id,
                    FullName = model.FullName ?? string.Empty,
                    BranchId = model.BranchId ?? 0,
                    Roles = model.Roles,
                    ApprovalLimit = model.ApprovalLimit,
                    IsActive = model.IsActive,
                    ConcurrencyStamp = model.ConcurrencyStamp ?? string.Empty,
                }, cancellationToken);
                FlashSuccess($"User {model.Email} updated.");
                return RedirectToAction(nameof(Index));
            }
            catch (ValidationException ex)
            {
                AddErrors(ModelState, ex);
            }
            catch (ConcurrencyConflictException ex)
            {
                FlashError(ex.Message);
                return RedirectToAction(nameof(Edit), new { id });
            }
        }

        return View("Form", await WithBranchesAsync(model, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(string id, CancellationToken cancellationToken)
    {
        await _users.UnlockUserAsync(id, cancellationToken);
        FlashSuccess("The account has been unlocked.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<UserFormViewModel> WithBranchesAsync(UserFormViewModel model, CancellationToken cancellationToken)
    {
        model.Branches = await _lookups.GetBranchesAsync(cancellationToken);
        return model;
    }
}

public sealed class UserFormViewModel
{
    public string? Id { get; set; }
    public string? ConcurrencyStamp { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string? Email { get; set; }

    [Required]
    [StringLength(150)]
    [Display(Name = "Full name")]
    public string? FullName { get; set; }

    [Required(ErrorMessage = "Select a branch.")]
    [Display(Name = "Branch")]
    public int? BranchId { get; set; }

    public List<string> Roles { get; set; } = new();

    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true)]
    [Display(Name = "Approval limit (LKR)")]
    public decimal? ApprovalLimit { get; set; }

    [DataType(DataType.Password)]
    [StringLength(128, MinimumLength = 12)]
    [Display(Name = "Temporary password")]
    public string? TemporaryPassword { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [BindNever]
    public IReadOnlyList<LookupItem> Branches { get; set; } = Array.Empty<LookupItem>();

    public bool IsEdit => !string.IsNullOrEmpty(Id);
}
