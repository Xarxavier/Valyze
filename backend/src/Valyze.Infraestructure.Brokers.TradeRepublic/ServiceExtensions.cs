using Microsoft.Extensions.DependencyInjection;
using Valyze.Domain.Application.Ingestion;

namespace Valyze.Infraestructure.Brokers.TradeRepublic;

public static class ServiceExtensions
{
    public static IServiceCollection AddValyzeBrokerTradeRepublic(this IServiceCollection services)
    {
        services.AddScoped<IBrokerAdapter, TradeRepublicPdfParser>();
        return services;
    }
}
