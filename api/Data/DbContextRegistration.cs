using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

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
                case AppDatabaseKind.MySql:
                {
                    var cs = connectivity.MySqlConnectionString!;
                    var serverVersion = ServerVersion.AutoDetect(cs);
                    options.UseMySql(cs, serverVersion);
                    break;
                }
                default:
                    throw new InvalidOperationException($"Unknown database kind: {connectivity.Kind}.");
            }
        });
    }
}
