using ApproveFlow.Application.Abstractions.Persistence;
using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Application.Common.Security;
using ApproveFlow.Application.Common.Validation;
using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Entities;
using ApproveFlow.Domain.Rules;
using FluentValidation;
using ValidationException = ApproveFlow.Application.Common.Exceptions.ValidationException;

namespace ApproveFlow.Application.Administration;

public sealed record BranchDto(int Id, string Code, string Name, string City, bool IsActive);

public sealed record SaveBranchRequest
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
}

public sealed record ProductDto(int Id, string Code, string Name, string? Description, decimal MinAmount, decimal MaxAmount,
    int MinTenureMonths, int MaxTenureMonths, decimal DefaultInterestRate, bool IsActive);

public sealed record SaveProductRequest
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal MinAmount { get; init; }
    public decimal MaxAmount { get; init; }
    public int MinTenureMonths { get; init; }
    public int MaxTenureMonths { get; init; }
    public decimal DefaultInterestRate { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class SaveBranchRequestValidator : AbstractValidator<SaveBranchRequest>
{
    public SaveBranchRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(10).Matches("^[A-Za-z0-9]+$").WithMessage("Code must be letters and digits only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
    }
}

public sealed class SaveProductRequestValidator : AbstractValidator<SaveProductRequest>
{
    public SaveProductRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(10).Matches("^[A-Za-z0-9]+$").WithMessage("Code must be letters and digits only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.MinAmount).GreaterThan(0);
        RuleFor(x => x.MaxAmount).GreaterThanOrEqualTo(x => x.MinAmount).WithMessage("Maximum amount must be at least the minimum amount.");
        RuleFor(x => x.MinTenureMonths).InclusiveBetween(LoanPolicy.MinTenureMonths, LoanPolicy.MaxTenureMonths);
        RuleFor(x => x.MaxTenureMonths).InclusiveBetween(LoanPolicy.MinTenureMonths, LoanPolicy.MaxTenureMonths)
            .GreaterThanOrEqualTo(x => x.MinTenureMonths).WithMessage("Maximum tenure must be at least the minimum tenure.");
        RuleFor(x => x.DefaultInterestRate).InclusiveBetween(0m, LoanPolicy.MaxInterestRate);
    }
}

public interface IReferenceDataAdminService
{
    Task<IReadOnlyList<BranchDto>> GetBranchesAsync(CancellationToken cancellationToken = default);
    Task<BranchDto> GetBranchAsync(int id, CancellationToken cancellationToken = default);
    Task<int> CreateBranchAsync(SaveBranchRequest request, CancellationToken cancellationToken = default);
    Task UpdateBranchAsync(int id, SaveBranchRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<ProductDto> GetProductAsync(int id, CancellationToken cancellationToken = default);
    Task<int> CreateProductAsync(SaveProductRequest request, CancellationToken cancellationToken = default);
    Task UpdateProductAsync(int id, SaveProductRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Admin maintenance of branches and loan products. Changes are captured by the audit trail automatically.</summary>
internal sealed class ReferenceDataAdminService : IReferenceDataAdminService
{
    private readonly IReferenceDataRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<SaveBranchRequest> _branchValidator;
    private readonly IValidator<SaveProductRequest> _productValidator;

    public ReferenceDataAdminService(IReferenceDataRepository repository, IUnitOfWork unitOfWork, ICurrentUser currentUser,
        IValidator<SaveBranchRequest> branchValidator, IValidator<SaveProductRequest> productValidator)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _branchValidator = branchValidator;
        _productValidator = productValidator;
    }

    public async Task<IReadOnlyList<BranchDto>> GetBranchesAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(AppRoles.Admin);
        return (await _repository.GetBranchesAsync(activeOnly: false, cancellationToken)).Select(ToDto).ToList();
    }

    public async Task<BranchDto> GetBranchAsync(int id, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(AppRoles.Admin);
        return ToDto(await _repository.GetBranchAsync(id, cancellationToken) ?? throw new NotFoundException("Branch", id));
    }

    public async Task<int> CreateBranchAsync(SaveBranchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _currentUser.RequireRole(AppRoles.Admin);
        await _branchValidator.ValidateOrThrowAsync(request, cancellationToken);
        if (await _repository.BranchCodeExistsAsync(request.Code, null, cancellationToken))
        {
            throw new ValidationException(nameof(request.Code), "A branch with this code already exists.");
        }

        var branch = new Branch(request.Code, request.Name, request.City);
        if (!request.IsActive)
        {
            branch.Update(request.Code, request.Name, request.City, isActive: false);
        }

        _repository.Add(branch);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return branch.Id;
    }

    public async Task UpdateBranchAsync(int id, SaveBranchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _currentUser.RequireRole(AppRoles.Admin);
        await _branchValidator.ValidateOrThrowAsync(request, cancellationToken);
        var branch = await _repository.GetBranchAsync(id, cancellationToken) ?? throw new NotFoundException("Branch", id);
        if (await _repository.BranchCodeExistsAsync(request.Code, id, cancellationToken))
        {
            throw new ValidationException(nameof(request.Code), "A branch with this code already exists.");
        }

        branch.Update(request.Code, request.Name, request.City, request.IsActive);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(AppRoles.Admin);
        return (await _repository.GetProductsAsync(activeOnly: false, cancellationToken)).Select(ToDto).ToList();
    }

    public async Task<ProductDto> GetProductAsync(int id, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(AppRoles.Admin);
        return ToDto(await _repository.GetProductAsync(id, cancellationToken) ?? throw new NotFoundException("Loan product", id));
    }

    public async Task<int> CreateProductAsync(SaveProductRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _currentUser.RequireRole(AppRoles.Admin);
        await _productValidator.ValidateOrThrowAsync(request, cancellationToken);
        if (await _repository.ProductCodeExistsAsync(request.Code, null, cancellationToken))
        {
            throw new ValidationException(nameof(request.Code), "A product with this code already exists.");
        }

        var product = Map(() => new LoanProduct(request.Code, request.Name, request.Description, request.MinAmount, request.MaxAmount,
            request.MinTenureMonths, request.MaxTenureMonths, request.DefaultInterestRate));
        if (!request.IsActive)
        {
            Apply(product, request);
        }

        _repository.Add(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return product.Id;
    }

    public async Task UpdateProductAsync(int id, SaveProductRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _currentUser.RequireRole(AppRoles.Admin);
        await _productValidator.ValidateOrThrowAsync(request, cancellationToken);
        var product = await _repository.GetProductAsync(id, cancellationToken) ?? throw new NotFoundException("Loan product", id);
        if (await _repository.ProductCodeExistsAsync(request.Code, id, cancellationToken))
        {
            throw new ValidationException(nameof(request.Code), "A product with this code already exists.");
        }

        Apply(product, request);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static void Apply(LoanProduct product, SaveProductRequest r) =>
        Map(() =>
        {
            product.Update(r.Code, r.Name, r.Description, r.MinAmount, r.MaxAmount, r.MinTenureMonths, r.MaxTenureMonths, r.DefaultInterestRate,
                r.IsActive);
            return product;
        });

    private static T Map<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (BusinessRuleViolationException ex)
        {
            throw new ValidationException(string.Empty, ex.Message);
        }
    }

    private static BranchDto ToDto(Branch b) => new(b.Id, b.Code, b.Name, b.City, b.IsActive);

    private static ProductDto ToDto(LoanProduct p) => new(p.Id, p.Code, p.Name, p.Description, p.MinAmount, p.MaxAmount, p.MinTenureMonths,
        p.MaxTenureMonths, p.DefaultInterestRate, p.IsActive);
}
