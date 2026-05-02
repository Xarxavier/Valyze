using Dapper;
using Microsoft.Extensions.Configuration;
using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Exceptions;
using Valyze.Domain.Money;
using Valyze.Domain.QueryService;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Infraestructure.QueryService.Portfolio;

public class PortfolioQueryService : BaseQueryService, IPortfolioQueryService
{
    public PortfolioQueryService(IConfiguration configuration) : base(configuration) { }

    private sealed record AccountRow(Guid AccountId, string BaseCurrency);
    private sealed record TradeAggRow(string PriceCurrency, short Side, decimal Notional);

    public async Task<PortfolioViewEntity> GetViewAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();

        var account = await connection.QueryFirstOrDefaultAsync<AccountRow>(
            "SELECT id AS AccountId, base_currency AS BaseCurrency FROM accounts WHERE id = @AccountId",
            new { AccountId = accountId })
            ?? throw new BusinessException("msnAccountNotFound");

        // Net cash flow per currency. Buys add (qty * price + fees), sells subtract (qty * price - fees).
        // Side enum: 1 = Buy, 2 = Sell.
        const string sql = @"
            SELECT
                price_currency AS PriceCurrency,
                side           AS Side,
                SUM(
                    CASE
                        WHEN side = 1 THEN quantity * price_amount + fees_amount
                        WHEN side = 2 THEN -1 * (quantity * price_amount - fees_amount)
                        ELSE 0
                    END
                ) AS Notional
            FROM trades
            WHERE account_id = @AccountId
            GROUP BY price_currency, side;";

        var rows = (await connection.QueryAsync<TradeAggRow>(sql, new { AccountId = accountId })).ToList();
        var tradeCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM trades WHERE account_id = @AccountId",
            new { AccountId = accountId });

        var baseCurrency = new Currency(account.BaseCurrency);
        var perCurrency = rows
            .GroupBy(r => r.PriceCurrency, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Notional), StringComparer.OrdinalIgnoreCase);

        var totalInvested = perCurrency.TryGetValue(baseCurrency.Code, out var baseAmount)
            ? new MoneyValue(baseAmount, baseCurrency)
            : new MoneyValue(0m, baseCurrency);

        var foreignTotals = perCurrency
            .Where(kv => !kv.Key.Equals(baseCurrency.Code, StringComparison.OrdinalIgnoreCase))
            .Select(kv => new MoneyValue(kv.Value, new Currency(kv.Key)))
            .ToList();

        var view = new PortfolioViewEntity
        {
            AccountId = account.AccountId,
            BaseCurrency = baseCurrency,
            PositionCount = 0,
            TradeCount = tradeCount,
            TotalInvested = totalInvested,
            ForeignTotals = foreignTotals,
        };

        return AccountGuard.EnforceSingle(view, accountId, v => v.AccountId);
    }

    public async Task<int> CountTradesAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM trades WHERE account_id = @AccountId",
            new { AccountId = accountId });
    }
}
