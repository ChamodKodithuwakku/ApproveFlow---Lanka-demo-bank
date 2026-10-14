using ApproveFlow.Application.Abstractions.Persistence;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Application.Common.Models;
using ApproveFlow.Application.Common.Security;
using ApproveFlow.Application.Common.Validation;
using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Logging;
using ValidationException = ApproveFlow.Application.Common.Exceptions.ValidationException;

namespace ApproveFlow.Application.Customers;

public sealed record CustomerListItemDto(int Id, string CustomerNumber, string FullName, string Nic, string BranchName, string? Phone,
    bool IsActive, DateTime CreatedAtUtc);

public sealed record CreateCustomerRequest
{
    public string FullName { get; init; } = string.Empty;
    public string Nic { get; init; } = string.Empty;
    public int BranchId { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
}

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Nic).NotEmpty().Must(nic => Customer.IsValidNic(Customer.NormaliseNic(nic)))
            .WithMessage("NIC must be 9 digits followed by V or X, or 12 digits.");
        RuleFor(x => x.BranchId).GreaterThan(0).WithMessage("Select a branch.");
        RuleFor(x => x.DateOfBirth)
            .Must(dob => dob is null || dob.Value <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddYears(-18))
            .WithMessage("The customer must be at least 18 years old.");
        RuleFor(x => x.Phone).MaximumLength(20).Matches(@"^\+?[0-9 ]{9,15}$").When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Enter a valid phone number.");
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Address).MaximumLength(300);
    }
}

public interface ICustomerService
{
    Task<PagedResult<CustomerListItemDto>> SearchAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);
}

internal sealed class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customers;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<CreateCustomerRequest> _validator;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(ICustomerRepository customers, IReferenceDataRepository referenceData, IUnitOfWork unitOfWork,
        ICurrentUser currentUser, IValidator<CreateCustomerRequest> validator, ILogger<CustomerService> logger)
    {
        _customers = customers;
        _referenceData = referenceData;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _validator = validator;
        _logger = logger;
    }

    public async Task<PagedResult<CustomerListItemDto>> SearchAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireActor();
        var result = await _customers.SearchAsync(search, page, pageSize, cancellationToken);
        return new PagedResult<CustomerListItemDto>(
            result.Items.Select(c => new CustomerListItemDto(c.Id, c.CustomerNumber, c.FullName, c.Nic, c.Branch?.Name ?? string.Empty, c.Phone,
                c.IsActive, c.CreatedAtUtc)).ToList(),
            result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<int> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = _currentUser.RequireRole(AppRoles.Maker, AppRoles.Admin);
        await _validator.ValidateOrThrowAsync(request, cancellationToken);

        if (await _referenceData.GetBranchAsync(request.BranchId, cancellationToken) is not { IsActive: true })
        {
            throw new ValidationException(nameof(request.BranchId), "Select an active branch.");
        }

        if (await _customers.NicExistsAsync(request.Nic, null, cancellationToken))
        {
            throw new ValidationException(nameof(request.Nic), "A customer with this NIC already exists.");
        }

        var number = await _customers.NextCustomerNumberAsync(cancellationToken);
        Customer customer;
        try
        {
            customer = new Customer(number, request.FullName, request.Nic, request.BranchId, request.DateOfBirth, request.Phone, request.Email,
                request.Address);
        }
        catch (BusinessRuleViolationException ex)
        {
            throw new ValidationException(ex.Code == "invalid_nic" ? nameof(request.Nic) : nameof(request.FullName), ex.Message);
        }

        _customers.Add(customer);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Customer {CustomerNumber} created by {UserId}", customer.CustomerNumber, actor.UserId);
        return customer.Id;
    }
}
