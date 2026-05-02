using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Valyze.Infraestructure.EntityFramework;

public static class ServiceExtensions
{
    public static IServiceCollection AddValyzeEntityFramework(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing connection string 'Postgres'.");

        services.AddDbContext<ValyzeDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly("Valyze.Infraestructure.EntityFramework")));

        return services;
    }
}
