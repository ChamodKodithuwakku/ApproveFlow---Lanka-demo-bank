namespace ApproveFlow.Web.Security;

/// <summary>
/// Adds defensive HTTP response headers to every response (OWASP Secure Headers Project).
/// The UI uses no inline script or style, so the Content-Security-Policy can be strict: only same-origin resources,
/// no plugins, no framing. Swagger UI (Development tooling) gets a relaxed policy because it ships inline code.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private const string StrictPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private const string SwaggerPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private static readonly string[] StaticPrefixes = { "/lib", "/css", "/js", "/favicon" };

    private readonly RequestDelegate _next;
    private readonly bool _upgradeInsecureRequests;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _next = next;
        _upgradeInsecureRequests = !environment.IsDevelopment();
    }

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var path = context.Request.Path;

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            var policy = path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase) ? SwaggerPolicy : StrictPolicy;
            headers.ContentSecurityPolicy = _upgradeInsecureRequests ? policy + "; upgrade-insecure-requests" : policy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";

            // Pages and API responses contain customer and loan data: never cache them in browsers or proxies.
            if (!StaticPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                headers.CacheControl = "no-store, max-age=0";
                headers.Pragma = "no-cache";
            }

            return Task.CompletedTask;
        });

        return _next(context);
    }
}
