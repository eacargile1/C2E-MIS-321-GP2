using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Data;

public static class DbContextRegistration
{
    public static void AddAppDbContext(this IServiceCollection services, DatabaseConnectivity connectivity)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            switch (connectivity.Kind)
            {
                case AppDatabaseKind.InMemory:
                    options.UseInMemoryDatabase(connectivity.InMemoryName!);
                    break;
                case AppDatabaseKind.Npgsql:
                    options.UseNpgsql(connectivity.NpgsqlConnectionString);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown database kind: {connectivity.Kind}.");
            }
        });
    }
}
