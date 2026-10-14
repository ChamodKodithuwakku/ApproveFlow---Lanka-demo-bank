using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ApproveFlow.Web.Api;

/// <summary>
/// Converts expected business failures thrown by API actions into ProblemDetails responses (4xx). Applied to every
/// API controller, so known failures are logged at Information level rather than as server errors.
/// </summary>
public sealed class ApiExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ApiExceptionFilter> _logger;

    public ApiExceptionFilter(ILogger<ApiExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (status, _, _) = ApiProblems.Classify(context.Exception);
        if (status == StatusCodes.Status500InternalServerError)
        {
            return; // Unexpected: let the global exception handler log it and return a generic 500.
        }

        _logger.LogInformation("API request {Path} rejected with {Status}: {Message}", context.HttpContext.Request.Path, status,
            context.Exception.Message);
        var problem = ApiProblems.Create(context.HttpContext, context.Exception);
        context.Result = new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
        context.ExceptionHandled = true;
    }
}

/// <summary>Last line of defence for API requests: unexpected exceptions become a generic 500 ProblemDetails (no stack trace).</summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (!httpContext.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return false; // UI requests fall through to the friendly error page.
        }

        _logger.LogError(exception, "Unhandled API exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        var problem = ApiProblems.Create(httpContext, exception);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", cancellationToken);
        return true;
    }
}
