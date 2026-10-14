using System.Globalization;
using ApproveFlow.Application;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Infrastructure;
using ApproveFlow.Infrastructure.Persistence;
using ApproveFlow.Web.Infrastructure;
using ApproveFlow.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

// Minimal console logger for failures before the host (and its configured Serilog pipeline) exists.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var isDevelopment = builder.Environment.IsDevelopment();

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(services.GetRequiredService<IConfiguration>())
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // Layers
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure();
    builder.Services.AddIdentityPersistence().AddClaimsPrincipalFactory<ApplicationClaimsPrincipalFactory>();

    // UI authentication: hardened Identity cookie. (__Host- prefix and Secure-only outside Development.)
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.Name = isDevelopment ? "ApproveFlow.Auth" : "__Host-ApproveFlow";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            // API callers get 401, never an HTML redirect.
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

    // Role changes / deactivation (which rotate the security stamp) sign the user out within 5 minutes.
    builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

    builder.Services.AddAuthorization(Policies.Configure);

    builder.Services.AddAntiforgery(options =>
    {
        options.Cookie.Name = isDevelopment ? "ApproveFlow.Csrf" : "__Host-ApproveFlow.Csrf";
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.HeaderName = "X-CSRF-TOKEN";
    });

    builder.Services.AddControllersWithViews(options =>
    {
        // Every unsafe (POST/PUT/DELETE) MVC request must carry a valid anti-forgery token.
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    });

    builder.Services.AddApproveFlowRateLimiting(builder.Configuration);

    var app = builder.Build();

    await app.Services.InitialiseDatabaseAsync();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");
    app.UseHttpsRedirection();

    var culture = new CultureInfo("en-GB");
    app.UseRequestLocalization(new RequestLocalizationOptions
    {
        DefaultRequestCulture = new RequestCulture(culture),
        SupportedCultures = new[] { culture },
        SupportedUICultures = new[] { culture },
    });

    app.UseStaticFiles();
    app.UseSerilogRequestLogging();
    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllerRoute(name: "areas", pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
    app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
    app.MapHealthChecks("/health").AllowAnonymous();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "ApproveFlow terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Entry point marker (also used by WebApplicationFactory in integration tests).</summary>
public partial class Program;
