using ContentEmpireSystem.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ContentEmpireSystem.Infrastructure.Data;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var apiRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "ContentEmpireSystem.Api"));
        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var (options, connectionString) = DatabaseConnectionResolver.Resolve(configuration, apiRoot);
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();

        if (options.IsSqlServer)
            builder.UseSqlServer(connectionString);
        else
            builder.UseSqlite(connectionString);

        return new ApplicationDbContext(builder.Options, new DesignTimeCurrentUserAccessor());
    }
}
