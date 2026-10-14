using ApproveFlow.Application.Common.Security;
using FluentValidation;

namespace ApproveFlow.Application.Administration;

public sealed record UserListItemDto(
    string Id,
    string Email,
    string FullName,
    int BranchId,
    string BranchName,
    decimal? ApprovalLimit,
    bool IsActive,
    bool IsLockedOut,
    DateTimeOffset? LockoutEnd,
    IReadOnlyList<string> Roles,
    string ConcurrencyStamp);

public abstract record UserInput
{
    public string FullName { get; init; } = string.Empty;
    public int BranchId { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    /// <summary>Required for Checkers (LKR). Ignored for other roles.</summary>
    public decimal? ApprovalLimit { get; init; }
}

public sealed record CreateUserRequest : UserInput
{
    public string Email { get; init; } = string.Empty;

    /// <summary>Initial password; must satisfy the password policy. The user should change it after first sign-in.</summary>
    public string TemporaryPassword { get; init; } = string.Empty;
}

public sealed record UpdateUserRequest : UserInput
{
    public string Id { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;

    /// <summary>Identity concurrency stamp read with the user; protects against overwriting another admin's change.</summary>
    public string ConcurrencyStamp { get; init; } = string.Empty;
}

public abstract class UserInputValidator<T> : AbstractValidator<T> where T : UserInput
{
    protected UserInputValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.BranchId).GreaterThan(0).WithMessage("Select a branch.");
        RuleFor(x => x.Roles).Must(roles => AppRoles.ValidateCombination(roles) is null)
            .WithMessage(x => AppRoles.ValidateCombination(x.Roles) ?? string.Empty);
        RuleFor(x => x.ApprovalLimit).NotNull().GreaterThan(0)
            .When(x => x.Roles.Contains(AppRoles.Checker))
            .WithMessage("Checkers must have an approval limit greater than zero.");
        RuleFor(x => x.ApprovalLimit).LessThanOrEqualTo(1_000_000_000m);
    }
}

public sealed class CreateUserRequestValidator : UserInputValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.TemporaryPassword).NotEmpty().MinimumLength(12);
    }
}

public sealed class UpdateUserRequestValidator : UserInputValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ConcurrencyStamp).NotEmpty();
    }
}

/// <summary>User and role administration (implemented in Infrastructure on top of ASP.NET Core Identity).</summary>
public interface IUserAdministrationService
{
    Task<IReadOnlyList<UserListItemDto>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<UserListItemDto> GetUserAsync(string id, CancellationToken cancellationToken = default);
    Task<string> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default);
    Task UnlockUserAsync(string id, CancellationToken cancellationToken = default);
}
