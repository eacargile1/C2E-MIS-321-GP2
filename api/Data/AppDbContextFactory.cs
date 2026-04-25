using System.Reflection;
using C2E.Api.Configuration;
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
        DotEnvFilePriority.HydrateProcessEnvironmentFromDevelopmentDotEnvFiles(apiProjectDir);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiProjectDir)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(DotEnvFilePriority.BuildConfigurationOverrides(apiProjectDir))
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
        static string? WalkUp(DirectoryInfo start)
        {
            for (var dir = start; dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "C2E.Api.csproj")))
                    return dir.FullName;

                var nestedApi = Path.Combine(dir.FullName, "api", "C2E.Api.csproj");
                if (File.Exists(nestedApi))
                    return Path.GetFullPath(Path.Combine(dir.FullName, "api"));
            }

            return null;
        }

        foreach (var anchor in new[]
                 {
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory,
                     Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                 })
        {
            if (string.IsNullOrWhiteSpace(anchor))
                continue;
            try
            {
                var found = WalkUp(new DirectoryInfo(anchor));
                if (found is not null)
                    return found;
            }
            catch
            {
                // ignore
            }
        }

        return Directory.GetCurrentDirectory();
    }
}
