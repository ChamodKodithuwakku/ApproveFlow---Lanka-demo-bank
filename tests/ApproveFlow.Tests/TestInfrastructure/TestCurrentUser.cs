using ApproveFlow.Application.Common.Interfaces;

namespace ApproveFlow.Tests.TestInfrastructure;

/// <summary>Settable current user for service-level tests.</summary>
public sealed class TestCurrentUser : ICurrentUser
{
    private HashSet<string> _roles = new(StringComparer.Ordinal);

    public string? UserId { get; private set; }
    public string? UserName { get; private set; }
    public string? FullName { get; private set; }
    public bool IsAuthenticated => UserId is not null;
    public int? BranchId { get; private set; }
    public string? IpAddress { get; set; } = "10.20.30.40";
    public string? CorrelationId { get; set; } = "test-correlation-id";

    public bool IsInRole(string role) => _roles.Contains(role);

    public void Set(string userId, string userName, string fullName, int branchId, IEnumerable<string> roles)
    {
        UserId = userId;
        UserName = userName;
        FullName = fullName;
        BranchId = branchId;
        _roles = new HashSet<string>(roles, StringComparer.Ordinal);
    }

    public void SignOut()
    {
        UserId = null;
        UserName = null;
        FullName = null;
        BranchId = null;
        _roles.Clear();
    }
}
