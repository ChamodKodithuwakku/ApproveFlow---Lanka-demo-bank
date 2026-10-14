using ApproveFlow.Application.Abstractions.Auditing;
using ApproveFlow.Infrastructure.Identity;
using ApproveFlow.Web.Models;
using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApproveFlow.Web.Controllers;

/// <summary>Cookie sign-in for the web UI. Every outcome is written to the audit trail as a security event.</summary>
public sealed class AccountController : AppController
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditTrail _auditTrail;
    private readonly ILogger<AccountController> _logger;

    public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, IAuditTrail auditTrail,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _auditTrail = auditTrail;
        _logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is { IsActive: false })
        {
            await _auditTrail.RecordSecurityEventAsync(SecurityEvents.LoginBlockedInactive, user.Id, model.Email, null, cancellationToken);
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            await _auditTrail.RecordSecurityEventAsync(SecurityEvents.LoginSucceeded, user!.Id, user.Email, new { Channel = "Web" }, cancellationToken);
            _logger.LogInformation("User {UserId} signed in", user.Id);

            // Open-redirect protection: only local return URLs are honoured.
            return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction("Index", "Home");
        }

        if (result.IsLockedOut)
        {
            await _auditTrail.RecordSecurityEventAsync(SecurityEvents.AccountLockedOut, user?.Id, model.Email,
                new { Channel = "Web", user?.LockoutEnd }, cancellationToken);
            _logger.LogWarning("Locked-out account {Email} attempted to sign in", model.Email);
            ModelState.AddModelError(string.Empty,
                "This account is temporarily locked after repeated failed sign-in attempts. Try again in 15 minutes or contact an administrator.");
            return View(model);
        }

        // Same message whether the email exists or not, to avoid account enumeration.
        await _auditTrail.RecordSecurityEventAsync(SecurityEvents.LoginFailed, user?.Id, model.Email,
            new { Channel = "Web", KnownUser = user is not null }, cancellationToken);
        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        await _signInManager.SignOutAsync();
        await _auditTrail.RecordSecurityEventAsync(SecurityEvents.Logout, userId, User.Identity?.Name, null, cancellationToken);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> AccessDenied(string? returnUrl, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            await _auditTrail.RecordSecurityEventAsync(SecurityEvents.AccessDenied, _userManager.GetUserId(User), User.Identity.Name,
                new { Path = returnUrl }, cancellationToken);
        }

        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    [HttpGet]
    [Authorize(Policy = Policies.AnyStaff)]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    [Authorize(Policy = Policies.AnyStaff)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        await _signInManager.RefreshSignInAsync(user);
        await _auditTrail.RecordSecurityEventAsync(SecurityEvents.PasswordChanged, user.Id, user.Email, null, cancellationToken);
        FlashSuccess("Your password has been changed.");
        return RedirectToAction("Index", "Home");
    }
}
