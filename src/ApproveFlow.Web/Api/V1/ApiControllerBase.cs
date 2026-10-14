using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApproveFlow.Web.Api.V1;

/// <summary>
/// Base for REST API v1 controllers: JWT bearer only (cookies are ignored, so the API is not exposed to CSRF),
/// per-client rate limiting, and RFC 7807 problem details for every error.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[IgnoreAntiforgeryToken]
[EnableRateLimiting(RateLimitPolicies.Api)]
[TypeFilter(typeof(ApiExceptionFilter))]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
public abstract class ApiControllerBase : ControllerBase
{
}
