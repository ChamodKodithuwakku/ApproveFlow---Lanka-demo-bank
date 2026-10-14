using System.ComponentModel.DataAnnotations;
using ApproveFlow.Application.Abstractions.Auditing;
using ApproveFlow.Infrastructure.Identity;
using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApproveFlow.Web.Api.V1;

/// <summary>Authentication for API clients.</summary>
[Route("api/v1/auth")]
[Tags("Authentication")]
public sealed class AuthController : ApiControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly JwtTokenService _tokens;
    private readonly IAuditTrail _auditTrail;

    public AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, JwtTokenService tokens,
        IAuditTrail auditTrail)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokens = tokens;
        _auditTrail = auditTrail;
    }

    /// <summary>Exchanges staff credentials for a short-lived JWT access token.</summary>
    /// <remarks>
    /// Use the returned <c>accessToken</c> as <c>Authorization: Bearer &lt;token&gt;</c> (or click "Authorize" in Swagger UI).
    /// Five consecutive failures lock the account for 15 minutes. Every outcome is written to the audit trail.
    /// Tokens are revoked automatically when the user is deactivated, locked out or has their roles changed.
    /// </remarks>
    /// <response code="200">Token issued.</response>
    /// <response code="400">Malformed request.</response>
    /// <response code="401">Invalid credentials, inactive or locked account.</response>
    /// <response code="429">Too many sign-in attempts from this client.</response>
    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateToken([FromBody] TokenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            await _auditTrail.RecordSecurityEventAsync(SecurityEvents.TokenDenied, user?.Id, request.Email,
                new { Channel = "API", Reason = user is null ? "UnknownUser" : "Inactive" }, cancellationToken);
            return InvalidCredentials();
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            await _auditTrail.RecordSecurityEventAsync(SecurityEvents.AccountLockedOut, user.Id, user.Email, new { Channel = "API" }, cancellationToken);
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Account locked",
                detail: "The account is temporarily locked after repeated failed attempts.");
        }

        if (!result.Succeeded)
        {
            await _auditTrail.RecordSecurityEventAsync(SecurityEvents.TokenDenied, user.Id, user.Email,
                new { Channel = "API", Reason = "InvalidPassword" }, cancellationToken);
            return InvalidCredentials();
        }

        var roles = await _userManager.GetRolesAsync(user);
        var (token, expiresAtUtc) = _tokens.Create(user, roles);
        await _auditTrail.RecordSecurityEventAsync(SecurityEvents.TokenIssued, user.Id, user.Email,
            new { Channel = "API", ExpiresAtUtc = expiresAtUtc }, cancellationToken);

        return Ok(new TokenResponse(token, "Bearer", expiresAtUtc, (int)(expiresAtUtc - DateTime.UtcNow).TotalSeconds, roles.ToList()));
    }

    private ObjectResult InvalidCredentials() =>
        Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials", detail: "Invalid email or password.");
}

/// <summary>Credentials for <c>POST /api/v1/auth/token</c>.</summary>
public sealed record TokenRequest
{
    /// <summary>Staff email, e.g. checker1@demo.lk.</summary>
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; init; } = string.Empty;

    /// <summary>Account password.</summary>
    [Required]
    [StringLength(128)]
    public string Password { get; init; } = string.Empty;
}

/// <summary>An issued access token.</summary>
/// <param name="AccessToken">JWT to send as a bearer token.</param>
/// <param name="TokenType">Always "Bearer".</param>
/// <param name="ExpiresAtUtc">Expiry instant (UTC).</param>
/// <param name="ExpiresIn">Seconds until expiry.</param>
/// <param name="Roles">Roles embedded in the token.</param>
public sealed record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc, int ExpiresIn, IReadOnlyList<string> Roles);
