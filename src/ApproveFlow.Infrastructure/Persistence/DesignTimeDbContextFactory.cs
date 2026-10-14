using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ApproveFlow.Infrastructure.Persistence;

/// <summary>
/// Used only by the dotnet-ef tooling (migrations add / script / bundle). Always targets SQL Server.
/// The connection string is taken from the ConnectionStrings__DefaultConnection environment variable;
/// `dotnet ef database update --connection "..."` overrides it explicitly.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost,1433;Database=ApproveFlow;Integrated Security=true;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .Options;

        return new ApplicationDbContext(options);
    }
}
