using ApproveFlow.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApproveFlow.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// SQL Server: applies migrations when Database:ApplyMigrationsOnStartup is true (Development); in production
    /// migrations are applied by the release pipeline (idempotent script or migration bundle).
    /// SQLite: creates the schema from the model. Then seeds demo data when Seed:Enabled is true (never in Production).
    /// </summary>
    public static async Task InitialiseDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        if (db.IsSqlServer)
        {
            if (options.ApplyMigrationsOnStartup)
            {
                logger.LogInformation("Applying EF Core migrations to SQL Server");
                await db.Database.MigrateAsync(cancellationToken);
            }
        }
        else
        {
            logger.LogWarning("Using SQLite ({Provider}); stored procedures, views and triggers are SQL Server only", db.Database.ProviderName);
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }

        await provider.GetRequiredService<DataSeeder>().SeedAsync(cancellationToken);
    }
}
