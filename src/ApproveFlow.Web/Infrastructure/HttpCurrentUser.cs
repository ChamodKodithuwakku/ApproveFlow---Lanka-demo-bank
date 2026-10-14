using System.Globalization;
using System.Security.Claims;
using ApproveFlow.Application.Common.Interfaces;

namespace ApproveFlow.Web.Infrastructure;

/// <summary>Reads the current user from the HTTP context. Works for both cookie (UI) and JWT (API) principals.</summary>
internal sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public string? UserId => Find(ClaimTypes.NameIdentifier, AppClaimTypes.Subject);

    public string? UserName => Find(ClaimTypes.Name, AppClaimTypes.Email, ClaimTypes.Email);

    public string? FullName => Find(AppClaimTypes.FullName);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public int? BranchId =>
        int.TryParse(Find(AppClaimTypes.BranchId), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;

    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId => _accessor.HttpContext?.TraceIdentifier;

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    private string? Find(params string[] claimTypes)
    {
        var principal = Principal;
        if (principal is null)
        {
            return null;
        }

        foreach (var type in claimTypes)
        {
            var value = principal.FindFirst(type)?.Value;
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }
}

/// <summary>Claim types issued by ApproveFlow (short JWT-style names; used for both cookies and tokens).</summary>
public static class AppClaimTypes
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string FullName = "full_name";
    public const string BranchId = "branch_id";
}
