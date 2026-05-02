using Microsoft.Extensions.DependencyInjection;
using Valyze.Domain.Repository;
using Valyze.Infraestructure.Repository.Identity;
using Valyze.Infraestructure.Repository.MarketData;
using Valyze.Infraestructure.Repository.News;
using Valyze.Infraestructure.Repository.Portfolio;

namespace Valyze.Infraestructure.Repository;

public static class ServiceExtensions
{
    public static IServiceCollection AddValyzeRepositories(this IServiceCollection services)
    {
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITradeRepository, TradeRepository>();
        services.AddScoped<IPriceQuoteRepository, PriceQuoteRepository>();
        services.AddScoped<INewsSourceRepository, NewsSourceRepository>();
        services.AddScoped<INewsArticleRepository, NewsArticleRepository>();
        return services;
    }
}
