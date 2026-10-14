using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Domain.Common;

namespace ApproveFlow.Application.Common.Interfaces;

/// <summary>The user and request context for the current operation (HTTP request, background job or test).</summary>
public interface ICurrentUser
{
    string? UserId { get; }

    /// <summary>Login name (email address).</summary>
    string? UserName { get; }

    string? FullName { get; }

    bool IsAuthenticated { get; }

    int? BranchId { get; }

    string? IpAddress { get; }

    /// <summary>Request trace identifier, written to audit records for correlation with application logs.</summary>
    string? CorrelationId { get; }

    bool IsInRole(string role);
}

public static class CurrentUserExtensions
{
    /// <summary>Returns the current user as a domain <see cref="Actor"/>, or throws if unauthenticated.</summary>
    public static Actor RequireActor(this ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsAuthenticated || string.IsNullOrEmpty(user.UserId))
        {
            throw new ForbiddenAccessException("You must be signed in to perform this action.");
        }

        return new Actor(user.UserId, user.FullName ?? user.UserName ?? user.UserId);
    }

    /// <summary>Server-side role check (defence in depth behind controller authorization policies).</summary>
    public static Actor RequireRole(this ICurrentUser user, params string[] roles)
    {
        var actor = user.RequireActor();
        if (!roles.Any(user.IsInRole))
        {
            throw new ForbiddenAccessException($"This action requires the {string.Join(" or ", roles)} role.");
        }

        return actor;
    }

    public static bool IsInAnyRole(this ICurrentUser user, params string[] roles) =>
        user is not null && roles.Any(user.IsInRole);
}
