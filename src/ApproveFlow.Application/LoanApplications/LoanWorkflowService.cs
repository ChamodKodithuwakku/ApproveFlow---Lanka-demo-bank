using System.Globalization;
using ApproveFlow.Application.Abstractions.Identity;
using ApproveFlow.Application.Abstractions.Persistence;
using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Application.Common.Security;
using ApproveFlow.Application.Common.Validation;
using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Entities;
using ApproveFlow.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace ApproveFlow.Application.LoanApplications;

public interface ILoanWorkflowService
{
    Task<WorkflowResult> SubmitAsync(int id, WorkflowActionRequest request, CancellationToken cancellationToken = default);
    Task<WorkflowResult> ApproveAsync(int id, WorkflowActionRequest request, CancellationToken cancellationToken = default);
    Task<WorkflowResult> RejectAsync(int id, WorkflowActionRequest request, CancellationToken cancellationToken = default);
    Task<WorkflowResult> ReturnForCorrectionAsync(int id, WorkflowActionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Maker-checker state transitions. Every rule is enforced here and in the domain model — never only in the UI:
/// role check → input validation → concurrency check → domain rules (four-eyes, limits, dual approval, remarks,
/// immutability) → atomic save (state change + ApprovalAction + AuditLog in one transaction).
/// </summary>
internal sealed class LoanWorkflowService : ILoanWorkflowService
{
    private readonly ILoanApplicationRepository _applications;
    private readonly IUserDirectory _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly IValidator<WorkflowActionRequest> _validator;
    private readonly ILogger<LoanWorkflowService> _logger;

    public LoanWorkflowService(ILoanApplicationRepository applications, IUserDirectory users, IUnitOfWork unitOfWork,
        ICurrentUser currentUser, TimeProvider clock, IValidator<WorkflowActionRequest> validator, ILogger<LoanWorkflowService> logger)
    {
        _applications = applications;
        _users = users;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _validator = validator;
        _logger = logger;
    }

    public Task<WorkflowResult> SubmitAsync(int id, WorkflowActionRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(id, request, AppRoles.Maker, remarksRequired: false, cancellationToken, (application, actor, now, _) =>
        {
            var resubmission = application.Status == LoanStatus.ReturnedForCorrection;
            application.Submit(actor, now);
            return resubmission
                ? $"Application {application.ReferenceNumber} resubmitted for approval."
                : $"Application {application.ReferenceNumber} submitted for approval.";
        });

    public Task<WorkflowResult> ApproveAsync(int id, WorkflowActionRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(id, request, AppRoles.Checker, remarksRequired: false, cancellationToken, (application, actor, now, limit) =>
        {
            var outcome = application.Approve(actor, limit, request.Remarks, now);
            return outcome == ApprovalOutcome.Approved
                ? $"Application {application.ReferenceNumber} approved."
                : string.Create(CultureInfo.InvariantCulture,
                    $"First approval recorded for {application.ReferenceNumber}. The amount exceeds your approval limit of LKR {limit:N2}, so a second checker must approve it.");
        });

    public Task<WorkflowResult> RejectAsync(int id, WorkflowActionRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(id, request, AppRoles.Checker, remarksRequired: true, cancellationToken, (application, actor, now, _) =>
        {
            application.Reject(actor, request.Remarks, now);
            return $"Application {application.ReferenceNumber} rejected.";
        });

    public Task<WorkflowResult> ReturnForCorrectionAsync(int id, WorkflowActionRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(id, request, AppRoles.Checker, remarksRequired: true, cancellationToken, (application, actor, now, _) =>
        {
            application.ReturnForCorrection(actor, request.Remarks, now);
            return $"Application {application.ReferenceNumber} returned to the maker for correction.";
        });

    private async Task<WorkflowResult> ExecuteAsync(int id, WorkflowActionRequest request, string requiredRole, bool remarksRequired,
        CancellationToken cancellationToken, Func<LoanApplication, Actor, DateTime, decimal, string> transition)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = _currentUser.RequireRole(requiredRole);

        if (remarksRequired)
        {
            await _validator.ValidateOrThrowAsync(request, cancellationToken, WorkflowRuleSets.RemarksRequired);
        }
        else
        {
            await _validator.ValidateOrThrowAsync(request, cancellationToken);
        }

        // Business attributes are read fresh from the database: a deactivated user or a lowered limit applies at once.
        var user = await _users.GetAsync(actor.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new ForbiddenAccessException("Your account is not active.");
        }

        var application = await _applications.GetForUpdateAsync(id, cancellationToken) ?? throw new NotFoundException("Loan application", id);
        LoanApplicationService.ExpectRowVersion(application, request.RowVersion, _applications);

        var fromStatus = application.Status;
        var message = ValidationExtensions.MapDomainErrors(() =>
            transition(application, actor, _clock.GetUtcNow().UtcDateTime, user.ApprovalLimit ?? 0m));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Loan application {Reference} moved from {FromStatus} to {ToStatus} by {UserId}",
            application.ReferenceNumber, fromStatus, application.Status, actor.UserId);

        return new WorkflowResult(application.Id, application.ReferenceNumber, application.Status, message,
            RowVersions.Encode(application.RowVersion));
    }
}
