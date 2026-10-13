using Microsoft.Extensions.Configuration;

namespace ContentEmpireSystem.Infrastructure.Data;

public static class DatabaseConnectionResolver
{
    public static (DatabaseOptions Options, string ConnectionString) Resolve(IConfiguration configuration, string contentRootPath)
    {
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();

        if (!options.IsSqlite && !options.IsSqlServer)
            throw new InvalidOperationException(
                $"Database:Provider must be 'Sqlite' or 'SqlServer'. Got '{options.Provider}'.");

        var key = options.IsSqlServer ? "SqlServer" : "Sqlite";
        var connectionString = configuration.GetConnectionString(key)
            ?? throw new InvalidOperationException($"Connection string '{key}' not found.");

        if (options.IsSqlite && !connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Sqlite connection string must include Data Source=.");

        if (options.IsSqlite && connectionString.Contains("Data Source=contentempire.db", StringComparison.OrdinalIgnoreCase))
        {
            var dbPath = Path.Combine(contentRootPath, "contentempire.db");
            connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate";
        }

        return (options, connectionString);
    }
}
