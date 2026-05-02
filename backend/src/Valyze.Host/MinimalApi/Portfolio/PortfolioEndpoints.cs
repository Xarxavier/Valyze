using Valyze.Domain.Application.Portfolio;
using Valyze.Domain.Entities.Identity;

namespace Valyze.Host.MinimalApi.Portfolio;

public static class PortfolioEndpoints
{
    public static RouteGroupBuilder MapPortfolioEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/portfolio")
            .WithTags("Portfolio")
            .RequireAuthorization();

        group.MapGet("/", async (
            IGetPortfolioUseCase useCase,
            AccessorClassEntity accessor,
            CancellationToken ct) =>
        {
            var view = await useCase.ExecuteAsync(accessor.AccountId, ct);
            return Results.Ok(new
            {
                accountId = view.AccountId,
                baseCurrency = view.BaseCurrency.Code,
                positionCount = view.PositionCount,
                tradeCount = view.TradeCount,
                totalInvested = new
                {
                    amount = view.TotalInvested.Amount,
                    currency = view.TotalInvested.Currency.Code,
                },
                foreignTotals = view.ForeignTotals
                    .Select(m => new { amount = m.Amount, currency = m.Currency.Code })
                    .ToArray(),
            });
        }).WithName("GetPortfolio");

        return group;
    }
}
