using System.ComponentModel.DataAnnotations;

namespace ApproveFlow.Web.Models;

public sealed class LoginViewModel
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [StringLength(128)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public sealed class ChangePasswordViewModel
{
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [StringLength(128, MinimumLength = 12)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "The new passwords do not match.")]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed record ErrorViewModel(string? RequestId, int StatusCode = 500)
{
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public string Title => StatusCode switch
    {
        404 => "Page not found",
        403 => "Access denied",
        429 => "Too many requests",
        _ => "Something went wrong",
    };

    public string Message => StatusCode switch
    {
        404 => "The page or record you requested does not exist or is no longer available.",
        403 => "Your role does not allow access to this page.",
        429 => "You have sent too many requests in a short time. Please wait a moment and try again.",
        _ => "An unexpected error occurred. The problem has been logged; please try again or contact support quoting the reference below.",
    };
}
