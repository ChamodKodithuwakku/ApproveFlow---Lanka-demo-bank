using ApproveFlow.Application.Abstractions.Auditing;
using ApproveFlow.Application.Administration;
using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Application.Common.Security;
using ApproveFlow.Application.Common.Validation;
using ApproveFlow.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = ApproveFlow.Application.Common.Exceptions.ValidationException;

namespace ApproveFlow.Infrastructure.Identity;

/// <summary>
/// Admin user management on ASP.NET Core Identity. Profile and role changes run in one transaction and are captured
/// by the audit interceptor (AspNetUsers / AspNetUserRoles); unlocks are recorded as security events.
/// </summary>
internal sealed class UserAdministrationService : IUserAdministrationService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditTrail _auditTrail;
    private readonly IValidator<CreateUserRequest> _createValidator;
    private readonly IValidator<UpdateUserRequest> _updateValidator;
    private readonly TimeProvider _clock;
    private readonly ILogger<UserAdministrationService> _logger;

    public UserAdministrationService(UserManager<ApplicationUser> userManager, ApplicationDbContext db, ICurrentUser currentUser,
        IAuditTrail auditTrail, IValidator<CreateUserRequest> createValidator, IValidator<UpdateUserRequest> updateValidator,
        TimeProvider clock, ILogger<UserAdministrationService> logger)
    {
        _userManager = userManager;
        _db = db;
        _currentUser = currentUser;
        _auditTrail = auditTrail;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UserListItemDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(AppRoles.Admin);
        var users = await _db.Users.AsNoTracking().Include(u => u.Branch).OrderBy(u => u.FullName).ToListAsync(cancellationToken);
        var roles = await RoleLookupAsync(null, cancellationToken);
        return users.Select(u => ToDto(u, roles)).ToList();
    }

    public async Task<UserListItemDto> GetUserAsync(string id, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(AppRoles.Admin);
        var user = await _db.Users.AsNoTracking().Include(u => u.Branch).FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);
        return ToDto(user, await RoleLookupAsync(id, cancellationToken));
    }

    public async Task<string> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var admin = _currentUser.RequireRole(AppRoles.Admin);
        await _createValidator.ValidateOrThrowAsync(request, cancellationToken);
        await EnsureActiveBranchAsync(request.BranchId, cancellationToken);

        if (await _userManager.FindByEmailAsync(request.Email) is not null)
        {
            throw new ValidationException(nameof(request.Email), "A user with this email already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            EmailConfirmed = true,
            FullName = request.FullName.Trim(),
            BranchId = request.BranchId,
            ApprovalLimit = request.Roles.Contains(AppRoles.Checker) ? request.ApprovalLimit : null,
            IsActive = true,
            CreatedAtUtc = _clock.GetUtcNow().UtcDateTime,
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        ThrowIfFailed(await _userManager.CreateAsync(user, request.TemporaryPassword), nameof(request.TemporaryPassword));
        ThrowIfFailed(await _userManager.AddToRolesAsync(user, request.Roles), nameof(request.Roles));
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("User {UserId} created by {AdminId} with roles {Roles}", user.Id, admin.UserId, request.Roles);
        return user.Id;
    }

    public async Task UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var admin = _currentUser.RequireRole(AppRoles.Admin);
        await _updateValidator.ValidateOrThrowAsync(request, cancellationToken);
        await EnsureActiveBranchAsync(request.BranchId, cancellationToken);

        var user = await _userManager.FindByIdAsync(request.Id) ?? throw new NotFoundException("User", request.Id);
        if (!string.Equals(user.ConcurrencyStamp, request.ConcurrencyStamp, StringComparison.Ordinal))
        {
            throw new ConcurrencyConflictException();
        }

        // An administrator cannot lock themselves out of administration.
        if (admin.Is(user.Id) && (!request.IsActive || !request.Roles.Contains(AppRoles.Admin)))
        {
            throw new ValidationException(nameof(request.Roles), "You cannot remove your own Admin role or deactivate your own account.");
        }

        var wasActive = user.IsActive;
        user.FullName = request.FullName.Trim();
        user.BranchId = request.BranchId;
        user.ApprovalLimit = request.Roles.Contains(AppRoles.Checker) ? request.ApprovalLimit : null;
        user.IsActive = request.IsActive;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        ThrowIfFailed(await _userManager.UpdateAsync(user), string.Empty);

        var currentRoles = await _userManager.GetRolesAsync(user);
        var toRemove = currentRoles.Except(request.Roles, StringComparer.Ordinal).ToList();
        var toAdd = request.Roles.Except(currentRoles, StringComparer.Ordinal).ToList();
        if (toRemove.Count > 0)
        {
            ThrowIfFailed(await _userManager.RemoveFromRolesAsync(user, toRemove), nameof(request.Roles));
        }

        if (toAdd.Count > 0)
        {
            ThrowIfFailed(await _userManager.AddToRolesAsync(user, toAdd), nameof(request.Roles));
        }

        // Role changes or deactivation invalidate existing sign-in cookies at the next security-stamp check.
        if (toRemove.Count > 0 || toAdd.Count > 0 || (wasActive && !user.IsActive))
        {
            ThrowIfFailed(await _userManager.UpdateSecurityStampAsync(user), string.Empty);
        }

        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("User {UserId} updated by {AdminId}", user.Id, admin.UserId);
    }

    public async Task UnlockUserAsync(string id, CancellationToken cancellationToken = default)
    {
        var admin = _currentUser.RequireRole(AppRoles.Admin);
        var user = await _userManager.FindByIdAsync(id) ?? throw new NotFoundException("User", id);
        ThrowIfFailed(await _userManager.SetLockoutEndDateAsync(user, null), string.Empty);
        ThrowIfFailed(await _userManager.ResetAccessFailedCountAsync(user), string.Empty);
        await _auditTrail.RecordSecurityEventAsync(SecurityEvents.AccountUnlocked, user.Id, user.Email,
            new { UnlockedBy = admin.UserId, UnlockedByName = admin.UserName }, cancellationToken);
        _logger.LogInformation("User {UserId} unlocked by {AdminId}", user.Id, admin.UserId);
    }

    private async Task EnsureActiveBranchAsync(int branchId, CancellationToken cancellationToken)
    {
        if (!await _db.Branches.AnyAsync(b => b.Id == branchId && b.IsActive, cancellationToken))
        {
            throw new ValidationException("BranchId", "Select an active branch.");
        }
    }

    private async Task<ILookup<string, string>> RoleLookupAsync(string? userId, CancellationToken cancellationToken)
    {
        var pairs = await (from ur in _db.UserRoles
                           join r in _db.Roles on ur.RoleId equals r.Id
                           where userId == null || ur.UserId == userId
                           select new { ur.UserId, Role = r.Name! }).ToListAsync(cancellationToken);
        return pairs.ToLookup(p => p.UserId, p => p.Role);
    }

    private UserListItemDto ToDto(ApplicationUser u, ILookup<string, string> roles) => new(
        u.Id,
        u.Email ?? string.Empty,
        u.FullName,
        u.BranchId,
        u.Branch?.Name ?? string.Empty,
        u.ApprovalLimit,
        u.IsActive,
        u.LockoutEnd.HasValue && u.LockoutEnd.Value > _clock.GetUtcNow(),
        u.LockoutEnd,
        roles[u.Id].OrderBy(r => r, StringComparer.Ordinal).ToList(),
        u.ConcurrencyStamp ?? string.Empty);

    private static void ThrowIfFailed(IdentityResult result, string field)
    {
        if (result.Succeeded)
        {
            return;
        }

        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
        {
            throw new ConcurrencyConflictException();
        }

        throw new ValidationException(new Dictionary<string, string[]>
        {
            [field] = result.Errors.Select(e => e.Description).ToArray(),
        });
    }
}
