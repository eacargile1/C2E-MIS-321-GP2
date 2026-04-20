using MySqlConnector;

namespace C2E.Api.Data;

public enum AppDatabaseKind
{
    InMemory,
    MySql,
}

public sealed record DatabaseConnectivity(AppDatabaseKind Kind, string? InMemoryName, string? MySqlConnectionString)
{
    /// <summary>
    /// Resolution order: explicit in-memory (tests) → <c>DATABASE_URL</c> (Heroku) → <c>ConnectionStrings:DefaultConnection</c> → in-memory fallback.
    /// </summary>
    public static DatabaseConnectivity Resolve(IConfiguration config)
    {
        var inMemoryName = config["Database:InMemoryName"];
        if (!string.IsNullOrWhiteSpace(inMemoryName))
            return new DatabaseConnectivity(AppDatabaseKind.InMemory, inMemoryName.Trim(), null);

        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        var conn = config.GetConnectionString("DefaultConnection");
        string? mysqlConn = null;
        if (!string.IsNullOrWhiteSpace(databaseUrl))
            mysqlConn = HerokuDatabaseUrl.ToMySqlConnectionString(databaseUrl);
        else if (!string.IsNullOrWhiteSpace(conn))
            mysqlConn = conn;

        if (!string.IsNullOrWhiteSpace(mysqlConn))
            return new DatabaseConnectivity(AppDatabaseKind.MySql, null, ApplyMySqlPoolLimits(mysqlConn.Trim(), config));

        var fallback = config["Database:InMemoryFallbackName"] ?? "c2e-dev";
        return new DatabaseConnectivity(AppDatabaseKind.InMemory, fallback, null);
    }

    /// <summary>
    /// Managed MySQL tiers often cap <c>max_user_connections</c> (e.g. 10 for the whole DB user across all clients).
    /// </summary>
    private static string ApplyMySqlPoolLimits(string connectionString, IConfiguration config)
    {
        // Default 4: many shared hosts cap the *MySQL user* at 10 connections total across every client
        // (local API + deployed API + teammate + MySQL Workbench). Leave headroom.
        var cap = config.GetValue("Database:MySqlMaxPoolSize", 4);
        if (cap < 1) cap = 1;
        if (cap > 128) cap = 128;

        var b = new MySqlConnectionStringBuilder(connectionString);
        var stated = (int)b.MaximumPoolSize;
        // Connector defaults ~100 when omitted; honor a tighter explicit value from the connection string.
        var effective = stated > 0 ? Math.Min(cap, stated) : cap;
        b.MaximumPoolSize = (uint)Math.Clamp(effective, 1, 128);
        b.MinimumPoolSize = 0;
        return b.ConnectionString;
    }
}
