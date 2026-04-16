namespace C2E.Api.Data;

/// <summary>
/// Parse Heroku-style database URLs into ADO.NET-style connection strings
/// (<c>postgres://</c> → Npgsql; <c>mysql://</c> / <c>mysql2://</c> → MySQL Connector).
/// </summary>
public static class HerokuDatabaseUrl
{
    public static string ToNpgsql(string databaseUrl)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
            throw new ArgumentException("DATABASE_URL is empty.", nameof(databaseUrl));

        var uri = new Uri(databaseUrl);
        if (uri.Scheme is not ("postgres" or "postgresql"))
            throw new InvalidOperationException($"DATABASE_URL scheme must be postgres or postgresql, got: {uri.Scheme}");

        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrEmpty(database))
            throw new InvalidOperationException("DATABASE_URL must include a database name in the path.");

        // Heroku Postgres requires TLS; Trust Server Certificate is typical for managed Postgres providers in class/demo setups.
        return $"Host={host};Port={port};Database={database};Username={user};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    }

    /// <summary>Parse Heroku-style <c>mysql://</c> / <c>mysql2://</c> URLs (e.g. Jaws DB) into a MySqlConnector connection string.</summary>
    public static string ToMySqlConnectionString(string databaseUrl)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
            throw new ArgumentException("DATABASE_URL is empty.", nameof(databaseUrl));

        Uri uri;
        try
        {
            uri = new Uri(databaseUrl);
        }
        catch (UriFormatException)
        {
            throw new InvalidOperationException("The value must be a valid absolute URL.");
        }

        if (uri.Scheme is not ("mysql" or "mysql2"))
            throw new InvalidOperationException(
                $"DATABASE_URL must use a mysql scheme (mysql or mysql2) for MySQL; got: {uri.Scheme}");

        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        if (string.IsNullOrEmpty(user))
            throw new InvalidOperationException("DATABASE_URL must include a non-empty username.");

        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 3306;
        var database = uri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrEmpty(database))
            throw new InvalidOperationException("DATABASE_URL must include a database name in the path.");

        return $"Server={host};Port={port};Database={database};User ID={user};Password={password};SslMode=Preferred";
    }
}
