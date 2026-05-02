using Microsoft.Extensions.DependencyInjection;
using Valyze.Domain.Application.MarketData;
using Valyze.Infraestructure.MarketData.Yahoo.Internal;

namespace Valyze.Infraestructure.MarketData.Yahoo;

public static class ServiceExtensions
{
    public static IServiceCollection AddValyzeMarketDataYahoo(this IServiceCollection services)
    {
        services.AddMemoryCache();

        services.AddHttpClient(YahooFinancePriceFeed.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://query1.finance.yahoo.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
            // Yahoo's public chart endpoint blocks default .NET UA; a mainstream UA
            // string + a real-looking Accept header keeps it happy.
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json,text/plain,*/*");
        });

        services.AddHttpClient(OpenFigiIsinResolver.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.openfigi.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "valyze/0.1 (+https://github.com/Xarxavier/Valyze)");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        services.AddScoped<IIsinTickerResolver, OpenFigiIsinResolver>();
        services.AddScoped<IPriceFeed, YahooFinancePriceFeed>();
        return services;
    }
}
