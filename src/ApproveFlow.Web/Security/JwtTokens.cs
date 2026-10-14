using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ApproveFlow.Infrastructure.Identity;
using ApproveFlow.Web.Api;
using ApproveFlow.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApproveFlow.Web.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "approveflow";
    public string Audience { get; set; } = "approveflow-api";

    /// <summary>HMAC-SHA256 key (at least 32 bytes). Supplied via user-secrets or environment variable only.</summary>
    public string? SigningKey { get; set; }

    public int ExpiryMinutes { get; set; } = 30;
}

/// <summary>Issues short-lived JWT access tokens for the REST API.</summary>
public sealed class JwtTokenService
{
    /// <summary>Claim carrying a hash of the user's Identity security stamp (rotated on role change, deactivation, password change).</summary>
    public const string StampClaim = "sst";

    private readonly JwtSigningKey _signingKey;
    private readonly JwtOptions _options;
    private readonly TimeProvider _clock;

    public JwtTokenService(JwtSigningKey signingKey, IOptions<JwtOptions> options, TimeProvider clock)
    {
        _signingKey = signingKey;
        _options = options.Value;
        _clock = clock;
    }

    public (string AccessToken, DateTime ExpiresAtUtc) Create(ApplicationUser user, IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = _clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(AppClaimTypes.Subject, user.Id),
            new(AppClaimTypes.Email, user.Email ?? string.Empty),
            new(AppClaimTypes.Name, user.Email ?? string.Empty),
            new(AppClaimTypes.FullName, user.FullName),
            new(AppClaimTypes.BranchId, user.BranchId.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(StampClaim, HashStamp(user.SecurityStamp)),
        };
        claims.AddRange(roles.Select(role => new Claim(AppClaimTypes.Role, role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(_signingKey.Key, SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    /// <summary>Only a hash travels in the (signed, not encrypted) token, never the stamp itself.</summary>
    public static string HashStamp(string? stamp) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp ?? string.Empty)))[..16];
}

/// <summary>
/// The symmetric signing key. Required outside Development; in Development an ephemeral random key is generated
/// when none is configured (tokens then stop working after a restart, which is acceptable for local use).
/// </summary>
public sealed class JwtSigningKey
{
    public JwtSigningKey(IOptions<JwtOptions> options, IHostEnvironment environment, ILogger<JwtSigningKey> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.Value.SigningKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Jwt:SigningKey is not configured. Provide a random key of at least 32 bytes via user-secrets or the Jwt__SigningKey environment variable.");
            }

            logger.LogWarning("Jwt:SigningKey not configured; using an ephemeral development key. Run scripts/setup-dev.ps1 to set one.");
            Key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(configured);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes (256 bits) long.");
        }

        Key = new SymmetricSecurityKey(bytes);
    }

    public SymmetricSecurityKey Key { get; }
}

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddApproveFlowJwt(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName);
        services.AddSingleton<JwtSigningKey>();
        services.AddSingleton<JwtTokenService>();

        services.AddAuthentication().AddJwtBearer();

        // Configured lazily so the key and issuer come from the final configuration (user-secrets, env, test hosts).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, JwtSigningKey, IHostEnvironment>((options, jwt, key, environment) =>
            {
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Value.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key.Key,
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = AppClaimTypes.Name,
                    RoleClaimType = AppClaimTypes.Role,
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateUserStillAllowedAsync,
                    OnChallenge = async context =>
                    {
                        // RFC 7807 body instead of an empty 401.
                        context.HandleResponse();
                        await ApiProblems.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "Unauthorized",
                            "A valid bearer token is required. Obtain one from POST /api/v1/auth/token.", "bearer_token_required",
                            wwwAuthenticate: string.IsNullOrEmpty(context.ErrorDescription)
                                ? "Bearer"
                                : $"Bearer error=\"invalid_token\", error_description=\"{context.ErrorDescription}\"");
                    },
                    OnForbidden = context => ApiProblems.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, "Forbidden",
                        "Your role does not permit this operation.", "forbidden_role"),
                };
            });

        return services;
    }

    /// <summary>
    /// Effective revocation: a token stops working as soon as the user is deactivated or locked out, or their security
    /// stamp changes (role change, password change, admin action) - not only when it expires.
    /// </summary>
    private static async Task ValidateUserStillAllowedAsync(TokenValidatedContext context)
    {
        var userId = context.Principal?.FindFirst(AppClaimTypes.Subject)?.Value;
        var stampHash = context.Principal?.FindFirst(JwtTokenService.StampClaim)?.Value;
        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = userId is null ? null : await users.FindByIdAsync(userId);

        if (user is null || !user.IsActive || await users.IsLockedOutAsync(user)
            || !string.Equals(stampHash, JwtTokenService.HashStamp(user.SecurityStamp), StringComparison.Ordinal))
        {
            context.Fail("The token has been revoked or the account is no longer active.");
        }
    }
}
