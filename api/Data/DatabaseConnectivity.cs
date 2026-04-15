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
            return new DatabaseConnectivity(AppDatabaseKind.MySql, null, mysqlConn.Trim());

        var fallback = config["Database:InMemoryFallbackName"] ?? "c2e-dev";
        return new DatabaseConnectivity(AppDatabaseKind.InMemory, fallback, null);
    }
}
