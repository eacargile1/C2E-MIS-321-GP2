using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace C2E.Api.Data;

public static class DbContextRegistration
{
    /// <summary>
    /// Avoid <see cref="ServerVersion.AutoDetect"/> — it opens its own DB connection(s) and can burn
    /// <c>max_user_connections</c> on small shared tiers before the pool even spins up.
    /// </summary>
    public static ServerVersion ResolveConfiguredMySqlServerVersion(IConfiguration configuration)
    {
        var s = configuration["Database:MySqlServerVersion"]?.Trim();
        if (!string.IsNullOrEmpty(s))
        {
            try
            {
                return ServerVersion.Parse(s);
            }
            catch (FormatException)
            {
                // fall through to default
            }
        }

        return new MySqlServerVersion(new Version(8, 0, 36));
    }

    public static void AddAppDbContext(
        this IServiceCollection services,
        DatabaseConnectivity connectivity,
        IConfiguration configuration)
    {
        var serverVersion =
            connectivity.Kind == AppDatabaseKind.MySql
                ? ResolveConfiguredMySqlServerVersion(configuration)
                : null;

        services.AddDbContext<AppDbContext>(options =>
        {
            switch (connectivity.Kind)
            {
                case AppDatabaseKind.InMemory:
                    options.UseInMemoryDatabase(connectivity.InMemoryName!);
                    break;
                case AppDatabaseKind.MySql:
                {
                    var cs = connectivity.MySqlConnectionString!;
                    options.UseMySql(cs, serverVersion!);
                    break;
                }
                default:
                    throw new InvalidOperationException($"Unknown database kind: {connectivity.Kind}.");
            }
        });
    }
}
