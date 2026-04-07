namespace C2E.Api.Data;

public enum AppDatabaseKind
{
    InMemory,
    Npgsql,
}

public sealed record DatabaseConnectivity(AppDatabaseKind Kind, string? InMemoryName, string? NpgsqlConnectionString)
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
        var npgsql = !string.IsNullOrWhiteSpace(databaseUrl)
            ? HerokuDatabaseUrl.ToNpgsql(databaseUrl)
            : conn;

        if (!string.IsNullOrWhiteSpace(npgsql))
            return new DatabaseConnectivity(AppDatabaseKind.Npgsql, null, npgsql.Trim());

        var fallback = config["Database:InMemoryFallbackName"] ?? "c2e-dev";
        return new DatabaseConnectivity(AppDatabaseKind.InMemory, fallback, null);
    }
}
