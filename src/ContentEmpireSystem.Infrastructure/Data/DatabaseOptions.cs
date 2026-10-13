namespace ContentEmpireSystem.Infrastructure.Data;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Sqlite (default, fast local) or SqlServer (production / legacy LocalDB).</summary>
    public string Provider { get; set; } = "Sqlite";

    /// <summary>When true, schema + seed run in background so Kestrel binds immediately.</summary>
    public bool BackgroundBootstrap { get; set; } = true;

    public int BootstrapTimeoutSeconds { get; set; } = 120;

    public bool IsSqlServer =>
        string.Equals(Provider, "SqlServer", StringComparison.OrdinalIgnoreCase);

    public bool IsSqlite =>
        string.Equals(Provider, "Sqlite", StringComparison.OrdinalIgnoreCase);
}
