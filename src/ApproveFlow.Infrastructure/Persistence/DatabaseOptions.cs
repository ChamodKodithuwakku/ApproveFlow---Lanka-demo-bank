using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ApproveFlow.Infrastructure.Persistence;

public enum DatabaseProvider
{
    /// <summary>Production and normal development (SQL Server 2019+ / Azure SQL).</summary>
    SqlServer = 0,

    /// <summary>Automated tests and the zero-install demo mode. No stored procedures, views or triggers.</summary>
    Sqlite = 1,
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;

    /// <summary>Apply pending EF Core migrations at startup (recommended for Development only).</summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Seed demo reference data, users and applications (never runs in Production).</summary>
    public bool Enabled { get; set; }

    /// <summary>Shared password for the demo accounts. Supplied via user-secrets or environment variable.</summary>
    public string? DemoPassword { get; set; }

    /// <summary>First administrator for a new (production) database. Bound from Bootstrap:AdminEmail.</summary>
    public string? BootstrapAdminEmail { get; set; }

    /// <summary>Initial password of the bootstrap administrator. Bound from Bootstrap:AdminPassword.</summary>
    public string? BootstrapAdminPassword { get; set; }
}

/// <summary>Includes the provider in the model cache key because the model differs slightly per provider.</summary>
internal sealed class ProviderAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), context.Database.ProviderName, designTime);
}
