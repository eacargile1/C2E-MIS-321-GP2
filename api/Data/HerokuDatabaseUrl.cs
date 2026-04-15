namespace C2E.Api.Data;

using MySqlConnector;

/// <summary>Parse Heroku-style <c>mysql://</c> / <c>mysql2://</c> URLs into a MySqlConnector connection string.</summary>
public static class HerokuDatabaseUrl
{
    /// <summary>Heroku MySQL add-ons typically require TLS; <c>SslMode=Preferred</c> matches common managed tiers.</summary>
    public static string ToMySqlConnectionString(string databaseUrl)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
            throw new ArgumentException("DATABASE_URL is empty.", nameof(databaseUrl));

        if (!Uri.TryCreate(databaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                "DATABASE_URL is not a valid absolute URL. Use a mysql:// URL from your add-on.");
        }

        if (uri.Scheme is not ("mysql" or "mysql2"))
        {
            throw new InvalidOperationException(
                $"DATABASE_URL must use the mysql scheme for this application (Heroku MySQL). Got scheme: {uri.Scheme}. " +
                "Use a mysql:// URL from your add-on, or omit DATABASE_URL and set ConnectionStrings:DefaultConnection.");
        }

        if (string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("DATABASE_URL must include credentials (user:password@).");

        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        if (string.IsNullOrWhiteSpace(user))
            throw new InvalidOperationException("DATABASE_URL must include a non-empty username.");

        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("DATABASE_URL must include a host.");

        var port = uri.Port > 0 ? uri.Port : 3306;
        var database = uri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrEmpty(database))
            throw new InvalidOperationException("DATABASE_URL must include a database name in the path.");

        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)port,
            Database = database,
            UserID = user,
            Password = password,
            // Heroku / managed MySQL: TLS expected; Preferred matches typical add-on behavior without hard-failing local tools.
            SslMode = MySqlSslMode.Preferred,
        };

        return builder.ConnectionString;
    }
}
