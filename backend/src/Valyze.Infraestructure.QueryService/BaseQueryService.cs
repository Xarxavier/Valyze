using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Valyze.Infraestructure.QueryService;

public abstract class BaseQueryService
{
    private readonly string _connectionString;

    protected BaseQueryService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing connection string 'Postgres'.");
    }

    protected NpgsqlConnection CreateConnection() => new(_connectionString);
}
