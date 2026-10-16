using ApproveFlow.Application.Common.Security;
using ApproveFlow.Infrastructure.Identity;
using ApproveFlow.Infrastructure.Persistence;
using ApproveFlow.Tests.TestInfrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApproveFlow.Tests.Workflow;

public sealed class BootstrapTests
{
    [Fact]
    public async Task First_administrator_is_created_once_from_configuration_without_demo_data()
    {
        await using var host = await SqliteTestHost.CreateAsync(seedDemoData: false, new Dictionary<string, string?>
        {
            ["Bootstrap:AdminEmail"] = "first.admin@bank.example",
            ["Bootstrap:AdminPassword"] = "Initial-Admin#2026",
        });

        // Running the initialiser again (next application start) must not create a second admin.
        await host.Services.InitialiseDatabaseAsync();

        var admins = await host.WithScopeAsync(sp => sp.GetRequiredService<UserManager<ApplicationUser>>().GetUsersInRoleAsync(AppRoles.Admin));
        Assert.Single(admins);
        Assert.Equal("first.admin@bank.example", admins[0].Email);

        // No demo users, customers or applications in a bootstrapped (production-style) database.
        Assert.Equal(1, await host.QueryAsync(db => db.Users.CountAsync()));
        Assert.Equal(0, await host.QueryAsync(db => db.LoanApplications.CountAsync()));
    }
}
