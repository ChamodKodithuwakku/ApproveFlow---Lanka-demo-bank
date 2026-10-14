namespace ApproveFlow.Domain.Common;

/// <summary>The authenticated user performing a domain operation.</summary>
public sealed record Actor
{
    public Actor(string userId, string userName)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("An actor must have a user id.", nameof(userId));
        }

        UserId = userId;
        UserName = string.IsNullOrWhiteSpace(userName) ? userId : userName;
    }

    public string UserId { get; }
    public string UserName { get; }

    public bool Is(string? userId) => !string.IsNullOrEmpty(userId) && string.Equals(UserId, userId, StringComparison.Ordinal);
}
