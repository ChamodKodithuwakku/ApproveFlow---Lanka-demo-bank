using System.Diagnostics;
using ApproveFlow.Application.Dashboard;
using ApproveFlow.Web.Models;
using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ApproveFlow.Web.Controllers;

[Authorize(Policy = Policies.AnyStaff)]
public sealed class HomeController : AppController
{
    private readonly IDashboardService _dashboard;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IDashboardService dashboard, ILogger<HomeController> logger)
    {
        _dashboard = dashboard;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(await _dashboard.GetAsync(cancellationToken));

    /// <summary>Friendly error page for unhandled exceptions (re-executed by the exception handler). Never shows details.</summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (feature is not null)
        {
            _logger.LogError(feature.Error, "Unhandled exception on {Path}", feature.Path);
        }

        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View("Error", new ErrorViewModel(Activity.Current?.Id ?? HttpContext.TraceIdentifier));
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int id)
    {
        var code = id is >= 400 and < 600 ? id : 500;
        Response.StatusCode = code;
        return View("Error", new ErrorViewModel(Activity.Current?.Id ?? HttpContext.TraceIdentifier, code));
    }
}
