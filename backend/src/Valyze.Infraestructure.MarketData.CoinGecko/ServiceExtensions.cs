using Microsoft.Extensions.DependencyInjection;
using Valyze.Domain.Application.MarketData;

namespace Valyze.Infraestructure.MarketData.CoinGecko;

public static class ServiceExtensions
{
    public static IServiceCollection AddValyzeMarketDataCoinGecko(this IServiceCollection services)
    {
        services.AddHttpClient(CoinGeckoPriceFeed.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.coingecko.com/api/v3/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "valyze/0.1 (+https://github.com/Xarxavier/Valyze)");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        services.AddScoped<IPriceFeed, CoinGeckoPriceFeed>();
        return services;
    }
}
