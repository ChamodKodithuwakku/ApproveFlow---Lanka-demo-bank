using ApproveFlow.Application;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Infrastructure;
using ApproveFlow.Infrastructure.Persistence;
using ApproveFlow.Web.Infrastructure;
using Serilog;

// Minimal console logger for failures before the host (and its configured Serilog pipeline) exists.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(services.GetRequiredService<IConfiguration>())
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure();
    builder.Services.AddIdentityPersistence();
    builder.Services.AddControllersWithViews();

    var app = builder.Build();

    await app.Services.InitialiseDatabaseAsync();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
    app.MapHealthChecks("/health");

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
