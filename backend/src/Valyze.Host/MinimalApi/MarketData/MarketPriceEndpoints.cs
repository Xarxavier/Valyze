using Valyze.Domain.QueryService;

namespace Valyze.Host.MinimalApi.MarketData;

public static class MarketPriceEndpoints
{
    public static RouteGroupBuilder MapMarketPriceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/market")
            .WithTags("Market")
            .RequireAuthorization();

        // GET /api/market/price?symbol={isin}
        // Returns the latest cached price for the given symbol in its native
        // quote currency, or 404 when no quote is available in the cache.
        group.MapGet("/price", async (
            IPriceQuoteQueryService priceService,
            string symbol,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return Results.BadRequest(new { reason = "symbol_required" });

            var money = await priceService.GetLatestForSymbolAsync(symbol, ct);
            if (money is null)
                return Results.NotFound(new { reason = "quote_unavailable" });

            return Results.Ok(new
            {
                symbol,
                amount = money.Value.Amount,
                currency = money.Value.Currency.Code,
            });
        }).WithName("GetMarketPrice");

        return group;
    }
}
