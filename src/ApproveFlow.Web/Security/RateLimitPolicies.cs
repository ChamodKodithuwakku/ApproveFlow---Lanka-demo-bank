using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApproveFlow.Web.Security;

/// <summary>Fixed-window rate limits: a strict one for credential endpoints and a per-client one for the REST API.</summary>
public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Api = "api";

    public static IServiceCollection AddApproveFlowRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(Login, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context, includeUser: false),
                _ => WindowOptions(configuration, "RateLimiting:Login", permitLimit: 10, windowSeconds: 300)));

            options.AddPolicy(Api, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context, includeUser: true),
                _ => WindowOptions(configuration, "RateLimiting:Api", permitLimit: 120, windowSeconds: 60)));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                var http = context.HttpContext;
                if (http.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    var problem = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Rate limit exceeded. Retry after the period indicated by the Retry-After header.",
                        Type = "https://tools.ietf.org/html/rfc6585#section-4",
                    };
                    problem.Extensions["traceId"] = http.TraceIdentifier;
                    await http.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
                }
                else
                {
                    http.Response.ContentType = "text/plain";
                    await http.Response.WriteAsync("Too many requests. Please wait a moment and try again.", cancellationToken);
                }
            };
        });

        return services;
    }

    private static string PartitionKey(HttpContext context, bool includeUser)
    {
        var user = includeUser && context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null;
        return user ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static FixedWindowRateLimiterOptions WindowOptions(IConfiguration configuration, string section, int permitLimit, int windowSeconds) => new()
    {
        PermitLimit = configuration.GetValue($"{section}:PermitLimit", permitLimit),
        Window = TimeSpan.FromSeconds(configuration.GetValue($"{section}:WindowSeconds", windowSeconds)),
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}
