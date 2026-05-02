using Microsoft.Extensions.DependencyInjection;
using Valyze.Domain.Application.MarketData;

namespace Valyze.Infraestructure.MarketData.Ecb;

public static class ServiceExtensions
{
    public static IServiceCollection AddValyzeMarketDataEcb(this IServiceCollection services)
    {
        services.AddMemoryCache();

        services.AddHttpClient(EcbFxFeed.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://www.ecb.europa.eu/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "valyze/0.1 (+https://github.com/Xarxavier/Valyze)");
            client.DefaultRequestHeaders.Add("Accept", "application/xml,text/xml,*/*");
        });

        services.AddScoped<IFxFeed, EcbFxFeed>();
        return services;
    }
}
