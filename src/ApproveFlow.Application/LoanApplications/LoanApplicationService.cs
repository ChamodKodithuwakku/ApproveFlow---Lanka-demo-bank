using ApproveFlow.Application.Abstractions.Identity;
using ApproveFlow.Application.Abstractions.Persistence;
using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Application.Common.Models;
using ApproveFlow.Application.Common.Security;
using ApproveFlow.Application.Common.Validation;
using ApproveFlow.Domain.Entities;
using ApproveFlow.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Logging;
using ValidationException = ApproveFlow.Application.Common.Exceptions.ValidationException;

namespace ApproveFlow.Application.LoanApplications;

public interface ILoanApplicationService
{
    Task<PagedResult<LoanApplicationListItemDto>> SearchAsync(LoanApplicationQuery query, CancellationToken cancellationToken = default);
    Task<LoanApplicationDetailsDto> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApprovalActionDto>> GetHistoryAsync(int id, CancellationToken cancellationToken = default);
    Task<LoanApplicationDetailsDto> CreateAsync(CreateLoanApplicationRequest request, CancellationToken cancellationToken = default);
    Task<LoanApplicationDetailsDto> UpdateAsync(int id, UpdateLoanApplicationRequest request, CancellationToken cancellationToken = default);
    Task DeleteDraftAsync(int id, string rowVersion, CancellationToken cancellationToken = default);
}

/// <summary>Create, edit, query and withdraw loan applications. State transitions live in <see cref="LoanWorkflowService"/>.</summary>
internal sealed class LoanApplicationService : ILoanApplicationService
{
    private readonly ILoanApplicationRepository _applications;
    private readonly ICustomerRepository _customers;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IReferenceNumberGenerator _references;
    private readonly IUserDirectory _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IValidator<CreateLoanApplicationRequest> _createValidator;
    private readonly IValidator<UpdateLoanApplicationRequest> _updateValidator;
    private readonly ILogger<LoanApplicationService> _logger;

    public LoanApplicationService(ILoanApplicationRepository applications, ICustomerRepository customers,
        IReferenceDataRepository referenceData, IReferenceNumberGenerator references, IUserDirectory users, IUnitOfWork unitOfWork,
        ICurrentUser currentUser, TimeProvider clock, IValidator<CreateLoanApplicationRequest> createValidator,
        IValidator<UpdateLoanApplicationRequest> updateValidator, ILogger<LoanApplicationService> logger)
    {
        _applications = applications;
        _customers = customers;
        _referenceData = referenceData;
        _references = references;
        _users = users;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _logger = logger;
    }

    public Task<PagedResult<LoanApplicationListItemDto>> SearchAsync(LoanApplicationQuery query, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireActor();
        return _applications.SearchAsync(query, cancellationToken);
    }

    public async Task<LoanApplicationDetailsDto> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var actor = _currentUser.RequireActor();
        var application = await _applications.GetDetailsAsync(id, cancellationToken)
            ?? throw new NotFoundException("Loan application", id);

        decimal? approvalLimit = null;
        if (_currentUser.IsInRole(AppRoles.Checker))
        {
            approvalLimit = (await _users.GetAsync(actor.UserId, cancellationToken))?.ApprovalLimit ?? 0m;
        }

        return LoanApplicationMapper.ToDetails(application, BuildPermissions(application, actor.UserId, approvalLimit));
    }

    public async Task<IReadOnlyList<ApprovalActionDto>> GetHistoryAsync(int id, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireActor();
        if (await _applications.GetDetailsAsync(id, cancellationToken) is null)
        {
            throw new NotFoundException("Loan application", id);
        }

        var history = await _applications.GetHistoryAsync(id, cancellationToken);
        return history.Select(LoanApplicationMapper.ToDto).ToList();
    }

    public async Task<LoanApplicationDetailsDto> CreateAsync(CreateLoanApplicationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var maker = _currentUser.RequireRole(AppRoles.Maker);
        await _createValidator.ValidateOrThrowAsync(request, cancellationToken);

        var (customer, product, branch) = await LoadPartiesAsync(request, cancellationToken);
        var now = _clock.GetUtcNow().UtcDateTime;
        var reference = await _references.NextAsync(now, cancellationToken);

        var application = ValidationExtensions.MapDomainErrors(() =>
            LoanApplication.CreateDraft(reference, customer, product, branch, ToTerms(request), maker, now));

        _applications.Add(application);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Loan application {Reference} created by {UserId}", application.ReferenceNumber, maker.UserId);

        return await GetAsync(application.Id, cancellationToken);
    }

    public async Task<LoanApplicationDetailsDto> UpdateAsync(int id, UpdateLoanApplicationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var maker = _currentUser.RequireRole(AppRoles.Maker);
        await _updateValidator.ValidateOrThrowAsync(request, cancellationToken);

        var application = await _applications.GetForUpdateAsync(id, cancellationToken) ?? throw new NotFoundException("Loan application", id);
        ExpectRowVersion(application, request.RowVersion);

        var (customer, product, branch) = await LoadPartiesAsync(request, cancellationToken);
        var now = _clock.GetUtcNow().UtcDateTime;
        ValidationExtensions.MapDomainErrors(() => application.UpdateDetails(customer, product, branch, ToTerms(request), maker, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Loan application {Reference} updated by {UserId}", application.ReferenceNumber, maker.UserId);
        return await GetAsync(application.Id, cancellationToken);
    }

    public async Task DeleteDraftAsync(int id, string rowVersion, CancellationToken cancellationToken = default)
    {
        var maker = _currentUser.RequireRole(AppRoles.Maker);
        var application = await _applications.GetForUpdateAsync(id, cancellationToken) ?? throw new NotFoundException("Loan application", id);
        ExpectRowVersion(application, rowVersion);
        application.SoftDelete(maker, _clock.GetUtcNow().UtcDateTime);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Draft loan application {Reference} withdrawn by {UserId}", application.ReferenceNumber, maker.UserId);
    }

    /// <summary>
    /// Optimistic concurrency: fail fast if the client's copy is stale, and make the UPDATE conditional on the
    /// version the client saw so a race between this read and the write is also detected by the database.
    /// </summary>
    internal static void ExpectRowVersion(LoanApplication application, string rowVersion, ILoanApplicationRepository repository)
    {
        if (!RowVersions.TryParse(rowVersion, out var expected))
        {
            throw new ValidationException("RowVersion", "RowVersion is not a valid concurrency token.");
        }

        if (!application.RowVersion.AsSpan().SequenceEqual(expected))
        {
            throw new ConcurrencyConflictException();
        }

        repository.SetExpectedRowVersion(application, expected);
    }

    private void ExpectRowVersion(LoanApplication application, string rowVersion) => ExpectRowVersion(application, rowVersion, _applications);

    private async Task<(Customer Customer, LoanProduct Product, Branch Branch)> LoadPartiesAsync(CreateLoanApplicationRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await _customers.GetAsync(request.CustomerId, cancellationToken)
            ?? throw new ValidationException(nameof(request.CustomerId), "The selected customer does not exist.");
        var product = await _referenceData.GetProductAsync(request.LoanProductId, cancellationToken)
            ?? throw new ValidationException(nameof(request.LoanProductId), "The selected loan product does not exist.");
        var branch = await _referenceData.GetBranchAsync(customer.BranchId, cancellationToken)
            ?? throw new ValidationException(nameof(request.CustomerId), "The customer's branch does not exist.");
        return (customer, product, branch);
    }

    private static LoanTerms ToTerms(CreateLoanApplicationRequest request) =>
        ValidationExtensions.MapDomainErrors(() => new LoanTerms(request.Amount, request.TenureMonths, request.InterestRate, request.Purpose,
            request.MonthlyIncome, request.ExistingObligations));

    private LoanApplicationPermissions BuildPermissions(LoanApplication application, string userId, decimal? approvalLimit)
    {
        var isMaker = _currentUser.IsInRole(AppRoles.Maker);
        var isChecker = _currentUser.IsInRole(AppRoles.Checker);
        var isOwner = string.Equals(application.CreatedById, userId, StringComparison.Ordinal);
        var editable = application.Status is LoanStatus.Draft or LoanStatus.ReturnedForCorrection;

        string? blockedReason = null;
        var canDecide = false;
        if (isChecker && application.Status is LoanStatus.Submitted or LoanStatus.PendingSecondApproval)
        {
            if (application.IsMakerOf(userId))
            {
                blockedReason = "Four-eyes principle: you created or last modified this application, so you cannot decide on it.";
            }
            else if (application.Status == LoanStatus.PendingSecondApproval && string.Equals(application.FirstApproverId, userId, StringComparison.Ordinal))
            {
                blockedReason = "You gave the first approval; a different checker must complete the second approval.";
            }
            else
            {
                canDecide = true;
            }
        }

        var needsSecondChecker = canDecide && application.Status == LoanStatus.Submitted && application.Amount > (approvalLimit ?? 0m);

        return new LoanApplicationPermissions(
            CanEdit: isMaker && isOwner && editable,
            CanSubmit: isMaker && isOwner && editable,
            CanDelete: isMaker && isOwner && application.Status == LoanStatus.Draft,
            CanDecide: canDecide,
            CanGenerateRiskSummary: _currentUser.IsInAnyRole(AppRoles.Checker, AppRoles.Auditor),
            DecisionBlockedReason: blockedReason,
            ApprovalNeedsSecondChecker: needsSecondChecker);
    }
}
