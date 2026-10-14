namespace ApproveFlow.Application.Abstractions.Identity;

/// <summary>Read access to user attributes that drive business rules (approval limit, branch, active flag).</summary>
public interface IUserDirectory
{
    Task<UserInfo?> GetAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed record UserInfo(
    string Id,
    string Email,
    string FullName,
    int BranchId,
    string? BranchName,
    decimal? ApprovalLimit,
    bool IsActive,
    IReadOnlyList<string> Roles);
