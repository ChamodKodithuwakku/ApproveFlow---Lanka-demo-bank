using ApproveFlow.Domain.Common;
using ApproveFlow.Domain.Entities;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Infrastructure.Identity;
using ApproveFlow.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ApproveFlow.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole, string>
{
    public const string ReferenceSequenceName = "LoanApplicationNumbers";

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<LoanProduct> LoanProducts => Set<LoanProduct>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<LoanApplication> LoanApplications => Set<LoanApplication>();
    public DbSet<ApprovalAction> ApprovalActions => Set<ApprovalAction>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportError> ImportErrors => Set<ImportError>();

    /// <summary>True for SQL Server (production). SQLite is used for tests and the no-install demo mode.</summary>
    public bool IsSqlServer => Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer";

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Money is decimal(18,2) unless a configuration says otherwise.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // Enums are stored as readable strings so stored procedures, views and ad-hoc DBA queries are self-explanatory.
        configurationBuilder.Properties<LoanStatus>().HaveConversion<string>().HaveMaxLength(30);
        configurationBuilder.Properties<ApprovalActionType>().HaveConversion<string>().HaveMaxLength(30);
        configurationBuilder.Properties<AuditEventType>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ImportBatchStatus>().HaveConversion<string>().HaveMaxLength(20);

        // All DateTime values are UTC; make sure they come back with DateTimeKind.Utc.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();

        if (!IsSqlServer)
        {
            // SQLite has no decimal type and cannot aggregate/sort decimals server-side; store as REAL.
            configurationBuilder.Properties<decimal>().HaveConversion<double>();
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        if (IsSqlServer)
        {
            // Native SQL Server rowversion and a sequence for gap-tolerant, concurrency-safe reference numbers.
            builder.Entity<LoanApplication>().Property(a => a.RowVersion).IsRowVersion();
            builder.HasSequence<long>(ReferenceSequenceName).StartsAt(1).IncrementsBy(1);
        }
        else
        {
            // Other providers: same optimistic-concurrency semantics with an application-maintained token
            // (renewed on every save by AuditableEntityInterceptor).
            builder.Entity<LoanApplication>().Property(a => a.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        }

        IdentityConfiguration.Configure(builder);
    }

    private sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                   v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }
}

/// <summary>Marker used by the audit interceptor to decide which tracked entities are written to AuditLog.</summary>
internal static class AuditScope
{
    public static bool IsAudited(object entity) =>
        entity is AuditableEntity or ApplicationUser or IdentityUserRole<string>;
}
