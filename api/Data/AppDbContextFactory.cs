using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace C2E.Api.Data;

/// <summary>Design-time factory for <c>dotnet ef</c> (migrations). Uses the API project directory as config root.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private static readonly string DesignTimePlaceholderConnectionString =
        "Server=127.0.0.1;Port=3306;Database=ef_design_placeholder;User ID=root;Password=root";

    private static readonly ServerVersion DesignTimeServerVersion = new MySqlServerVersion(new Version(8, 0, 36));

    public AppDbContext CreateDbContext(string[] args)
    {
        var apiProjectDir = FindApiProjectDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiProjectDir)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        var connectivity = DatabaseConnectivity.Resolve(configuration);
        var explicitInMemory = !string.IsNullOrWhiteSpace(configuration["Database:InMemoryName"]);

        if (connectivity.Kind == AppDatabaseKind.MySql)
        {
            var cs = connectivity.MySqlConnectionString!;
            var serverVersion = DbContextRegistration.ResolveConfiguredMySqlServerVersion(configuration);
            optionsBuilder.UseMySql(cs, serverVersion);
        }
        else if (connectivity.Kind == AppDatabaseKind.InMemory && explicitInMemory)
        {
            optionsBuilder.UseInMemoryDatabase(connectivity.InMemoryName!);
        }
        else
        {
            // No relational config: keep MySQL model snapshot for `dotnet ef` (mirrors relational provider, not in-memory fallback).
            optionsBuilder.UseMySql(DesignTimePlaceholderConnectionString, DesignTimeServerVersion);
        }

        return new AppDbContext(optionsBuilder.Options);
    }

    private static string FindApiProjectDirectory()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "C2E.Api.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
