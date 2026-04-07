using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace C2E.Api.Data;

/// <summary>Design-time factory for <c>dotnet ef</c> (migrations). Uses the API project directory as config root.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
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
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        var conn = configuration.GetConnectionString("DefaultConnection");

        if (!string.IsNullOrWhiteSpace(databaseUrl))
            optionsBuilder.UseNpgsql(HerokuDatabaseUrl.ToNpgsql(databaseUrl));
        else if (!string.IsNullOrWhiteSpace(conn))
            optionsBuilder.UseNpgsql(conn);
        else
        {
            // Never contacted during `migrations add`; only used so EF selects the Npgsql provider for the model snapshot.
            optionsBuilder.UseNpgsql(
                "Host=127.0.0.1;Port=5432;Database=ef_design_placeholder;Username=postgres;Password=postgres");
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
