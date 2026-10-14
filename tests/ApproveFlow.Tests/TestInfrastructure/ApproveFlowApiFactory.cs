using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApproveFlow.Tests.TestInfrastructure;

/// <summary>Hosts the real web application (Program.cs) in memory against a private, seeded SQLite database.</summary>
public sealed class ApproveFlowApiFactory : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly string _connectionString = $"Data Source=file:api-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private readonly SqliteConnection _keepAlive;

    public ApproveFlowApiFactory()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Sqlite"] = _connectionString,
            ["Seed:Enabled"] = "true",
            ["Seed:DemoPassword"] = SqliteTestHost.DemoPassword,
            ["Jwt:SigningKey"] = "integration-tests-signing-key-0123456789-abcdefghijklmnopqrstuvwxyz",
            ["Swagger:Enabled"] = "true",
            ["RateLimiting:Api:PermitLimit"] = "100000",
            ["RateLimiting:Login:PermitLimit"] = "100000",
            ["Serilog:MinimumLevel:Default"] = "Warning",
        }));
        builder.ConfigureTestServices(services => services.Configure<PasswordHasherOptions>(o => o.IterationCount = 1_000));
    }

    public async Task<string> GetTokenAsync(string email, string? password = null)
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/token", new { email, password = password ?? SqliteTestHost.DemoPassword });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    public async Task<HttpClient> CreateClientForAsync(string email)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(email));
        return client;
    }

    public async Task<T> WithServicesAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _keepAlive.Dispose();
        }
    }
}
