using C2E.Api.Data;

namespace C2E.Api.Tests;

public class HerokuDatabaseUrlTests
{
    [Fact]
    public void ToMySqlConnectionString_parses_heroku_style_mysql_url()
    {
        const string url = "mysql://user:secret%40word@ec2.example.com:3306/dbnameprod";
        var cs = HerokuDatabaseUrl.ToMySqlConnectionString(url);
        Assert.Contains("Server=ec2.example.com", cs);
        Assert.Contains("Port=3306", cs);
        Assert.Contains("Database=dbnameprod", cs);
        Assert.Contains("User ID=user", cs);
        Assert.Contains("Password=secret@word", cs);
        Assert.Contains("SslMode=Preferred", cs);
    }

    [Fact]
    public void ToMySqlConnectionString_accepts_mysql2_scheme()
    {
        const string url = "mysql2://u:p@h:3306/d";
        var cs = HerokuDatabaseUrl.ToMySqlConnectionString(url);
        Assert.Contains("Server=h", cs);
        Assert.Contains("Database=d", cs);
    }

    [Fact]
    public void ToMySqlConnectionString_rejects_non_mysql_scheme()
    {
        const string url = "postgres://u:p@h:5432/db";
        var ex = Assert.Throws<InvalidOperationException>(() => HerokuDatabaseUrl.ToMySqlConnectionString(url));
        Assert.Contains("mysql scheme", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToMySqlConnectionString_rejects_empty_url()
    {
        Assert.Throws<ArgumentException>(() => HerokuDatabaseUrl.ToMySqlConnectionString(""));
    }

    [Fact]
    public void ToMySqlConnectionString_rejects_malformed_url()
    {
        const string url = "not a url";
        var ex = Assert.Throws<InvalidOperationException>(() => HerokuDatabaseUrl.ToMySqlConnectionString(url));
        Assert.Contains("valid absolute URL", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToMySqlConnectionString_rejects_empty_username()
    {
        const string url = "mysql://:pass@db.example.com:3306/appdb";
        var ex = Assert.Throws<InvalidOperationException>(() => HerokuDatabaseUrl.ToMySqlConnectionString(url));
        Assert.Contains("non-empty username", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
