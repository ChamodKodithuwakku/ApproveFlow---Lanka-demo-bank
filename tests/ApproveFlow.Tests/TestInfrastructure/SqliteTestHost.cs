using ApproveFlow.Application;
using ApproveFlow.Application.Common.Interfaces;
using ApproveFlow.Application.Common.Validation;
using ApproveFlow.Application.LoanApplications;
using ApproveFlow.Domain.Enums;
using ApproveFlow.Infrastructure;
using ApproveFlow.Infrastructure.Identity;
using ApproveFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ApproveFlow.Tests.TestInfrastructure;

/// <summary>
/// Builds the real Application + Infrastructure + Identity service graph against a private SQLite in-memory
/// database (seeded with the demo data set), so service tests exercise the same code paths as production.
/// </summary>
public sealed class SqliteTestHost : IAsyncDisposable
{
    public const string DemoPassword = "Test-Password#2026";

    private readonly SqliteConnection _keepAlive;

    private SqliteTestHost(ServiceProvider services, SqliteConnection keepAlive, TestCurrentUser currentUser)
    {
        Services = services;
        _keepAlive = keepAlive;
        CurrentUser = currentUser;
    }

    public ServiceProvider Services { get; }
    public TestCurrentUser CurrentUser { get; }

    public static async Task<SqliteTestHost> CreateAsync(bool seedDemoData = true, IDictionary<string, string?>? extraConfiguration = null)
    {
        // A named shared-cache in-memory database lives as long as at least one connection is open.
        var connectionString = $"Data Source=file:approveflow-{Guid.NewGuid():N}?mode=memory&cache=shared";
        var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Sqlite"] = connectionString,
                ["Seed:Enabled"] = seedDemoData.ToString(),
                ["Seed:DemoPassword"] = DemoPassword,
            })
            .AddInMemoryCollection(extraConfiguration ?? new Dictionary<string, string?>())
            .Build();

        var currentUser = new TestCurrentUser();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddSingleton(currentUser);
        services.AddSingleton<ICurrentUser>(currentUser);
        services.AddApplication();
        services.AddInfrastructure();
        services.AddIdentityPersistence();
        // Cheap password hashing keeps the suite fast; production uses the framework default.
        services.Configure<PasswordHasherOptions>(o => o.IterationCount = 1_000);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
        await provider.InitialiseDatabaseAsync();
        return new SqliteTestHost(provider, keepAlive, currentUser);
    }

    /// <summary>Makes the given demo user the current user (optionally with extra roles, e.g. to test four-eyes).</summary>
    public async Task<ApplicationUser> SignInAsync(string email, params string[] extraRoles)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException($"Unknown test user {email}");
        var roles = (await users.GetRolesAsync(user)).Concat(extraRoles);
        CurrentUser.Set(user.Id, user.Email!, user.FullName, user.BranchId, roles);
        return user;
    }

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public async Task WithScopeAsync(Func<IServiceProvider, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    public Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query) =>
        WithScopeAsync(sp => query(sp.GetRequiredService<ApplicationDbContext>()));

    public Task<LoanApplicationDetailsDto> GetApplicationAsync(int id) =>
        WithScopeAsync(sp => sp.GetRequiredService<ILoanApplicationService>().GetAsync(id));

    /// <summary>Creates a draft as the given maker and (optionally) submits it. Returns the latest details.</summary>
    public async Task<LoanApplicationDetailsDto> CreateApplicationAsync(string makerEmail, decimal amount, bool submit = true,
        string productCode = "SME", int tenureMonths = 60)
    {
        await SignInAsync(makerEmail);
        var (customerId, productId) = await QueryAsync(async db =>
        (
            await db.Customers.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync(),
            await db.LoanProducts.Where(p => p.Code == productCode).Select(p => p.Id).FirstAsync()
        ));

        var created = await WithScopeAsync(sp => sp.GetRequiredService<ILoanApplicationService>().CreateAsync(new CreateLoanApplicationRequest
        {
            CustomerId = customerId,
            LoanProductId = productId,
            Amount = amount,
            TenureMonths = tenureMonths,
            InterestRate = 12.5m,
            Purpose = "Construction of a family home for integration testing",
            MonthlyIncome = 450_000m,
            ExistingObligations = 25_000m,
        }));

        if (!submit)
        {
            return created;
        }

        await WithScopeAsync(sp => sp.GetRequiredService<ILoanWorkflowService>()
            .SubmitAsync(created.Id, new WorkflowActionRequest { RowVersion = created.RowVersion }));
        var submitted = await GetApplicationAsync(created.Id);
        Assert.Equal(LoanStatus.Submitted, submitted.Status);
        return submitted;
    }

    public Task<WorkflowResult> ActAsync(int id, Func<ILoanWorkflowService, Func<int, WorkflowActionRequest, CancellationToken, Task<WorkflowResult>>> pick,
        string rowVersion, string? remarks = null) =>
        WithScopeAsync(sp => pick(sp.GetRequiredService<ILoanWorkflowService>())(id,
            new WorkflowActionRequest { RowVersion = rowVersion, Remarks = remarks }, CancellationToken.None));

    public static string Encode(byte[] rowVersion) => RowVersions.Encode(rowVersion);

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "ApproveFlow.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
