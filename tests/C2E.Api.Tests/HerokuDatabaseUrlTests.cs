using C2E.Api.Data;

namespace C2E.Api.Tests;

public class HerokuDatabaseUrlTests
{
    [Fact]
    public void ToNpgsql_parses_heroku_style_postgres_url()
    {
        const string url = "postgres://user:secret%40word@ec2.example.com:5432/dbnameprod";
        var cs = HerokuDatabaseUrl.ToNpgsql(url);
        Assert.Contains("Host=ec2.example.com", cs);
        Assert.Contains("Port=5432", cs);
        Assert.Contains("Database=dbnameprod", cs);
        Assert.Contains("Username=user", cs);
        Assert.Contains("Password=secret@word", cs);
        Assert.Contains("SSL Mode=Require", cs);
    }
}
