using Valyze.Domain.Application.Portfolio;
using Valyze.Domain.Entities.Identity;

namespace Valyze.Host.MinimalApi.Positions;

public static class PositionsEndpoints
{
    public static RouteGroupBuilder MapPositionsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/positions")
            .WithTags("Positions")
            .RequireAuthorization();

        group.MapGet("/", async (
            IGetPositionsUseCase useCase,
            AccessorClassEntity accessor,
            CancellationToken ct) =>
        {
            var view = await useCase.ExecuteAsync(accessor.AccountId, ct);

            return Results.Ok(new
            {
                accountId = view.AccountId,
                asOf = view.Summary.AsOf,
                baseCurrency = view.Summary.BaseCurrency.Code,
                summary = new
                {
                    totalInvested = ToMoney(view.Summary.TotalInvested),
                    totalCurrentValue = ToMoney(view.Summary.TotalCurrentValue),
                    totalUnrealizedPnl = ToMoney(view.Summary.TotalUnrealizedPnl),
                    totalRealizedPnl = ToMoney(view.Summary.TotalRealizedPnl),
                    totalPnl = ToMoney(view.Summary.TotalPnl),
                    openPositionCount = view.Summary.OpenPositionCount,
                    tradeCount = view.Summary.TradeCount,
                    valuationCoverage = view.Summary.ValuationCoverage,
                    foreignTotalsInvested = view.Summary.ForeignTotalsInvested
                        .Select(ToMoney).ToArray(),
                },
                positions = view.Positions.Select(p => new
                {
                    symbol = p.Instrument.Value,
                    name = p.Name,
                    quantity = p.Quantity,
                    avgCost = ToMoney(p.AvgCost),
                    totalCost = ToMoney(p.TotalCost),
                    realizedPnl = ToMoney(p.RealizedPnl),
                    valued = p.Valued,
                    currentPrice = p.CurrentPrice is { } cp ? ToMoney(cp) : null,
                    currentValue = p.CurrentValue is { } cv ? ToMoney(cv) : null,
                    unrealizedPnl = p.UnrealizedPnl is { } u ? ToMoney(u) : null,
                    unrealizedPnlPercent = p.UnrealizedPnlPercent,
                    estimatedSellCommission = p.EstimatedSellCommission is { } esc ? ToMoney(esc) : null,
                    netCurrentValue = p.NetCurrentValue is { } ncv ? ToMoney(ncv) : null,
                    netUnrealizedPnl = p.NetUnrealizedPnl is { } nu ? ToMoney(nu) : null,
                    tradeCount = p.TradeCount,
                    firstTradeAt = p.FirstTradeAt,
                    lastTradeAt = p.LastTradeAt,
                    trades = p.Trades.Select(t => new
                    {
                        id = t.Id,
                        executedAt = t.ExecutedAt,
                        side = t.Side.ToString(),
                        quantity = t.Quantity,
                        price = ToMoney(t.Price),
                        fees = ToMoney(t.Fees),
                        brokerKey = t.BrokerKey,
                        brokerReference = t.BrokerReference,
                    }).ToArray(),
                }).ToArray(),
            });
        }).WithName("GetPositions");

        return group;
    }

    private static object ToMoney(Valyze.Domain.Money.Money m) => new
    {
        amount = m.Amount,
        currency = m.Currency.Code,
    };
}
