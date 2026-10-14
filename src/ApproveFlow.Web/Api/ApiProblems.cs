using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ApproveFlow.Web.Api;

/// <summary>
/// Single mapping from failures to HTTP status codes and RFC 7807 problem details for the REST API.
/// 400 malformed request · 401 no/invalid token · 403 role or segregation-of-duties · 404 not found ·
/// 409 concurrency or invalid state transition · 422 validation / business rule · 500 unexpected (no details leaked).
/// </summary>
public static class ApiProblems
{
    public static (int Status, string Title, string? Code) Classify(Exception exception) => exception switch
    {
        ValidationException => (StatusCodes.Status422UnprocessableEntity, "Validation failed", "validation_failed"),
        BusinessRuleViolationException e => (StatusCodes.Status422UnprocessableEntity, "Business rule violated", e.Code),
        NotFoundException => (StatusCodes.Status404NotFound, "Not found", "not_found"),
        ForbiddenAccessException => (StatusCodes.Status403Forbidden, "Forbidden", "forbidden"),
        SegregationOfDutiesException e => (StatusCodes.Status403Forbidden, "Segregation of duties", e.Code),
        ConcurrencyConflictException => (StatusCodes.Status409Conflict, "Record was modified", "concurrency_conflict"),
        InvalidStatusTransitionException e => (StatusCodes.Status409Conflict, "Invalid state transition", e.Code),
        ConflictException => (StatusCodes.Status409Conflict, "Conflict", "conflict"),
        BadHttpRequestException => (StatusCodes.Status400BadRequest, "Bad request", "bad_request"),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", null),
    };

    public static ProblemDetails Create(HttpContext httpContext, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var (status, title, code) = Classify(exception);
        var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();

        ProblemDetails problem;
        if (exception is ValidationException validation)
        {
            var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
            foreach (var (field, messages) in validation.Errors)
            {
                foreach (var message in messages)
                {
                    modelState.AddModelError(field, message);
                }
            }

            problem = factory.CreateValidationProblemDetails(httpContext, modelState, status, title, detail: validation.Message);
        }
        else
        {
            // Business messages are safe to return; unexpected exceptions never expose their message or stack trace.
            var detail = status == StatusCodes.Status500InternalServerError
                ? "The error has been logged. Quote the traceId when contacting support."
                : exception.Message;
            problem = factory.CreateProblemDetails(httpContext, status, title, detail: detail);
        }

        if (code is not null)
        {
            problem.Extensions["code"] = code;
        }

        return problem;
    }

    public static async Task WriteAsync(HttpContext httpContext, int status, string title, string detail, string code, string? wwwAuthenticate = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(httpContext, status, title, detail: detail);
        problem.Extensions["code"] = code;
        httpContext.Response.StatusCode = status;
        if (wwwAuthenticate is not null)
        {
            httpContext.Response.Headers.WWWAuthenticate = wwwAuthenticate;
        }

        await httpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
}
