using Valyze.Host.MinimalApi.Auth;
using Valyze.Host.MinimalApi.Decisions;
using Valyze.Host.MinimalApi.Health;
using Valyze.Host.MinimalApi.MarketData;
using Valyze.Host.MinimalApi.News;
using Valyze.Host.MinimalApi.Portfolio;
using Valyze.Host.MinimalApi.Positions;
using Valyze.Host.MinimalApi.Trades;

namespace Valyze.Host.MinimalApi;

public static class MapMinimalApiExtensions
{
    public static WebApplication MapMinimalApi(this WebApplication app)
    {
        app.MapHealthEndpoints();
        app.MapAuthEndpoints();
        app.MapPortfolioEndpoints();
        app.MapPositionsEndpoints();
        app.MapTradesEndpoints();
        app.MapNewsEndpoints();
        app.MapDecisionEndpoints();
        app.MapMarketPriceEndpoints();
        return app;
    }
}
