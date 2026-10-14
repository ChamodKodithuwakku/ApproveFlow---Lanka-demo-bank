using System.ComponentModel.DataAnnotations;
using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Application.Common.Models;
using ApproveFlow.Application.Customers;
using ApproveFlow.Application.LoanApplications;
using ApproveFlow.Application.Lookups;
using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ValidationException = ApproveFlow.Application.Common.Exceptions.ValidationException;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ApproveFlow.Web.Controllers;

[Authorize(Policy = Policies.AnyStaff)]
public sealed class CustomersController : AppController
{
    private readonly ICustomerService _customers;
    private readonly ILookupService _lookups;

    public CustomersController(ICustomerService customers, ILookupService lookups)
    {
        _customers = customers;
        _lookups = lookups;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        ViewData["Search"] = search;
        return View(await _customers.SearchAsync(search, page, Paging.DefaultPageSize, cancellationToken));
    }

    [HttpGet]
    [Authorize(Policy = Policies.CustomerManager)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View(new CustomerFormViewModel { Branches = await _lookups.GetBranchesAsync(cancellationToken) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CustomerManager)]
    public async Task<IActionResult> Create(CustomerFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (ModelState.IsValid)
        {
            try
            {
                await _customers.CreateAsync(new CreateCustomerRequest
                {
                    FullName = model.FullName ?? string.Empty,
                    Nic = model.Nic ?? string.Empty,
                    BranchId = model.BranchId ?? 0,
                    DateOfBirth = model.DateOfBirth,
                    Phone = model.Phone,
                    Email = model.Email,
                    Address = model.Address,
                }, cancellationToken);
                FlashSuccess($"Customer {model.FullName} created.");
                return RedirectToAction(nameof(Index), new { search = model.Nic });
            }
            catch (ValidationException ex)
            {
                AddErrors(ModelState, ex);
            }
        }

        model.Branches = await _lookups.GetBranchesAsync(cancellationToken);
        return View(model);
    }
}

public sealed class CustomerFormViewModel
{
    [Required]
    [StringLength(150)]
    [Display(Name = "Full name")]
    public string? FullName { get; set; }

    [Required]
    [RegularExpression(@"^(\d{9}[VvXx]|\d{12})$", ErrorMessage = "NIC must be 9 digits followed by V or X, or 12 digits.")]
    [Display(Name = "NIC number")]
    public string? Nic { get; set; }

    [Required(ErrorMessage = "Select a branch.")]
    [Display(Name = "Home branch")]
    public int? BranchId { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Date of birth")]
    public DateOnly? DateOfBirth { get; set; }

    [StringLength(20)]
    [Phone]
    public string? Phone { get; set; }

    [StringLength(256)]
    [EmailAddress]
    public string? Email { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [BindNever]
    public IReadOnlyList<LookupItem> Branches { get; set; } = Array.Empty<LookupItem>();
}
