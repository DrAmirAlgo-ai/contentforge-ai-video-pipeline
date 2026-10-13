using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ContentEmpireSystem.Infrastructure.Data;

/// <summary>Enables WAL + NORMAL sync on SQLite for faster reads/writes.</summary>
public sealed class SqlitePerformanceInterceptor : DbConnectionInterceptor
{
    private static int _pragmaApplied;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ApplyPragmas(connection);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        if (Interlocked.Exchange(ref _pragmaApplied, 1) == 1)
            return;

        if (!connection.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Exchange(ref _pragmaApplied, 0);
            return;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }
}
