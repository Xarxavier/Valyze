using Microsoft.Extensions.DependencyInjection;
using Valyze.Domain.QueryService;
using Valyze.Infraestructure.QueryService.Decisions;
using Valyze.Infraestructure.QueryService.MarketData;
using Valyze.Infraestructure.QueryService.News;
using Valyze.Infraestructure.QueryService.Portfolio;

namespace Valyze.Infraestructure.QueryService;

public static class ServiceExtensions
{
    public static IServiceCollection AddValyzeQueryServices(this IServiceCollection services)
    {
        services.AddScoped<IPortfolioQueryService, PortfolioQueryService>();
        services.AddScoped<ITradeQueryService, TradeQueryService>();
        services.AddScoped<IPriceQuoteQueryService, PriceQuoteQueryService>();
        services.AddScoped<INewsSourceQueryService, NewsSourceQueryService>();
        services.AddScoped<INewsArticleQueryService, NewsArticleQueryService>();
        services.AddScoped<IInvestmentDecisionQueryService, InvestmentDecisionQueryService>();
        return services;
    }
}
