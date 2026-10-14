using System.ComponentModel.DataAnnotations;
using ApproveFlow.Application.Administration;
using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Web.Controllers;
using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ValidationException = ApproveFlow.Application.Common.Exceptions.ValidationException;

namespace ApproveFlow.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class BranchesController : AppController
{
    private readonly IReferenceDataAdminService _service;

    public BranchesController(IReferenceDataAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(await _service.GetBranchesAsync(cancellationToken));

    [HttpGet]
    public IActionResult Create() => View("Form", new BranchFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BranchFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (ModelState.IsValid)
        {
            try
            {
                await _service.CreateBranchAsync(model.ToRequest(), cancellationToken);
                FlashSuccess($"Branch {model.Name} created.");
                return RedirectToAction(nameof(Index));
            }
            catch (ValidationException ex)
            {
                AddErrors(ModelState, ex);
            }
        }

        return View("Form", model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        try
        {
            var b = await _service.GetBranchAsync(id, cancellationToken);
            return View("Form", new BranchFormViewModel { Id = b.Id, Code = b.Code, Name = b.Name, City = b.City, IsActive = b.IsActive });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, BranchFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Id = id;
        if (ModelState.IsValid)
        {
            try
            {
                await _service.UpdateBranchAsync(id, model.ToRequest(), cancellationToken);
                FlashSuccess($"Branch {model.Name} updated.");
                return RedirectToAction(nameof(Index));
            }
            catch (ValidationException ex)
            {
                AddErrors(ModelState, ex);
            }
        }

        return View("Form", model);
    }
}

[Area("Admin")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class ProductsController : AppController
{
    private readonly IReferenceDataAdminService _service;

    public ProductsController(IReferenceDataAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(await _service.GetProductsAsync(cancellationToken));

    [HttpGet]
    public IActionResult Create() => View("Form", new ProductFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (ModelState.IsValid)
        {
            try
            {
                await _service.CreateProductAsync(model.ToRequest(), cancellationToken);
                FlashSuccess($"Product {model.Name} created.");
                return RedirectToAction(nameof(Index));
            }
            catch (ValidationException ex)
            {
                AddErrors(ModelState, ex);
            }
        }

        return View("Form", model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        try
        {
            var p = await _service.GetProductAsync(id, cancellationToken);
            return View("Form", new ProductFormViewModel
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Description = p.Description,
                MinAmount = p.MinAmount,
                MaxAmount = p.MaxAmount,
                MinTenureMonths = p.MinTenureMonths,
                MaxTenureMonths = p.MaxTenureMonths,
                DefaultInterestRate = p.DefaultInterestRate,
                IsActive = p.IsActive,
            });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Id = id;
        if (ModelState.IsValid)
        {
            try
            {
                await _service.UpdateProductAsync(id, model.ToRequest(), cancellationToken);
                FlashSuccess($"Product {model.Name} updated.");
                return RedirectToAction(nameof(Index));
            }
            catch (ValidationException ex)
            {
                AddErrors(ModelState, ex);
            }
        }

        return View("Form", model);
    }
}

public sealed class BranchFormViewModel
{
    public int? Id { get; set; }

    [Required]
    [StringLength(10)]
    [RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "Letters and digits only.")]
    public string? Code { get; set; }

    [Required]
    [StringLength(100)]
    public string? Name { get; set; }

    [Required]
    [StringLength(100)]
    public string? City { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public SaveBranchRequest ToRequest() => new() { Code = Code ?? string.Empty, Name = Name ?? string.Empty, City = City ?? string.Empty, IsActive = IsActive };
}

public sealed class ProductFormViewModel
{
    public int? Id { get; set; }

    [Required]
    [StringLength(10)]
    [RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "Letters and digits only.")]
    public string? Code { get; set; }

    [Required]
    [StringLength(100)]
    public string? Name { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [Required]
    [Range(typeof(decimal), "1", "1000000000", ParseLimitsInInvariantCulture = true)]
    [Display(Name = "Minimum amount (LKR)")]
    public decimal? MinAmount { get; set; }

    [Required]
    [Range(typeof(decimal), "1", "1000000000", ParseLimitsInInvariantCulture = true)]
    [Display(Name = "Maximum amount (LKR)")]
    public decimal? MaxAmount { get; set; }

    [Required]
    [Range(1, 360)]
    [Display(Name = "Minimum tenure (months)")]
    public int? MinTenureMonths { get; set; }

    [Required]
    [Range(1, 360)]
    [Display(Name = "Maximum tenure (months)")]
    public int? MaxTenureMonths { get; set; }

    [Required]
    [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true)]
    [Display(Name = "Default interest rate (% p.a.)")]
    public decimal? DefaultInterestRate { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public SaveProductRequest ToRequest() => new()
    {
        Code = Code ?? string.Empty,
        Name = Name ?? string.Empty,
        Description = Description,
        MinAmount = MinAmount ?? 0,
        MaxAmount = MaxAmount ?? 0,
        MinTenureMonths = MinTenureMonths ?? 0,
        MaxTenureMonths = MaxTenureMonths ?? 0,
        DefaultInterestRate = DefaultInterestRate ?? 0,
        IsActive = IsActive,
    };
}
