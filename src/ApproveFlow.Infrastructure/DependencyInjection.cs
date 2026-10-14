using ApproveFlow.Application.Abstractions.Auditing;
using ApproveFlow.Application.Abstractions.Identity;
using ApproveFlow.Application.Abstractions.Persistence;
using ApproveFlow.Infrastructure.Auditing;
using ApproveFlow.Infrastructure.Identity;
using ApproveFlow.Infrastructure.Persistence;
using ApproveFlow.Infrastructure.Persistence.Interceptors;
using ApproveFlow.Infrastructure.Persistence.Repositories;
using ApproveFlow.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ApproveFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName);
        services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName);

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<AuditTrailInterceptor>();

        // Options are resolved lazily so test hosts and deployment environments can override configuration.
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var database = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            if (database.Provider == DatabaseProvider.Sqlite)
            {
                options.UseSqlite(configuration.GetConnectionString("Sqlite") ?? "Data Source=approveflow-demo.db");
            }
            else
            {
                var connectionString = configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException(
                        "ConnectionStrings:DefaultConnection is not configured. Set it with " +
                        "'dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"<connection string>\" --project src/ApproveFlow.Web', " +
                        "the ConnectionStrings__DefaultConnection environment variable, or run scripts/setup-dev.ps1.");
                }

                options.UseSqlServer(connectionString, sql =>
                {
                    sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    sql.CommandTimeout(60);
                });
            }

            options.AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>(), sp.GetRequiredService<AuditTrailInterceptor>());
            options.ReplaceService<IModelCacheKeyFactory, ProviderAwareModelCacheKeyFactory>();
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ILoanApplicationRepository, LoanApplicationRepository>();
        services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IReferenceNumberGenerator, ReferenceNumberGenerator>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<DataSeeder>();

        services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("database");

        return services;
    }

    /// <summary>
    /// ASP.NET Core Identity with the bank's password and lockout policy.
    /// Cookie paths and API (JWT) authentication are configured by the Web host.
    /// </summary>
    public static IdentityBuilder AddIdentityPersistence(this IServiceCollection services) =>
        services
            .AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredUniqueChars = 4;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
}
