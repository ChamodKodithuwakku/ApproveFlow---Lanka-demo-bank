using ApproveFlow.Application.Common.Exceptions;
using ApproveFlow.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ApproveFlow.Web.Controllers;

/// <summary>Base class for MVC (UI) controllers: flash messages and mapping of business errors to the UI.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
public abstract class AppController : Controller
{
    public const string SuccessKey = "af.success";
    public const string ErrorKey = "af.error";
    public const string InfoKey = "af.info";

    protected void FlashSuccess(string message) => TempData[SuccessKey] = message;

    protected void FlashError(string message) => TempData[ErrorKey] = message;

    protected void FlashInfo(string message) => TempData[InfoKey] = message;

    /// <summary>
    /// Copies field errors from an application <see cref="ValidationException"/> into ModelState, so the form
    /// re-renders with the server-side (authoritative) validation messages next to each field.
    /// </summary>
    protected static void AddErrors(ModelStateDictionary modelState, ValidationException exception, string? prefix = null)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        ArgumentNullException.ThrowIfNull(exception);
        foreach (var (field, messages) in exception.Errors)
        {
            foreach (var message in messages)
            {
                modelState.AddModelError(string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(field) ? field : $"{prefix}.{field}", message);
            }
        }
    }

    /// <summary>True for business-rule failures whose message is safe and useful to show to the user.</summary>
    protected static bool IsUserFacing(Exception exception) =>
        exception is DomainException or AppException;
}
