using Dapper;
using Microsoft.Extensions.Configuration;
using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Enum;
using Valyze.Domain.Exceptions;
using Valyze.Domain.Instruments;
using Valyze.Domain.Money;
using Valyze.Domain.QueryService;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Infraestructure.QueryService.Portfolio;

public class TradeQueryService : BaseQueryService, ITradeQueryService
{
    public TradeQueryService(IConfiguration configuration) : base(configuration) { }

    private sealed record TradeRow(
        Guid Id,
        Guid AccountId,
        string Instrument,
        short Side,
        decimal Quantity,
        decimal PriceAmount,
        string PriceCurrency,
        decimal FeesAmount,
        string FeesCurrency,
        DateTime ExecutedAt,
        string BrokerKey,
        string? BrokerReference,
        string? Name);

    public async Task<IReadOnlyList<TradeEntity>> ListByAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();

        const string sql = @"
            SELECT
                id               AS Id,
                account_id       AS AccountId,
                instrument       AS Instrument,
                side             AS Side,
                quantity         AS Quantity,
                price_amount     AS PriceAmount,
                price_currency   AS PriceCurrency,
                fees_amount      AS FeesAmount,
                fees_currency    AS FeesCurrency,
                executed_at      AS ExecutedAt,
                broker_key       AS BrokerKey,
                broker_reference AS BrokerReference,
                instrument_name  AS Name
            FROM trades
            WHERE account_id = @AccountId
            ORDER BY executed_at, id;";

        var rows = (await connection.QueryAsync<TradeRow>(sql, new { AccountId = accountId })).ToList();

        return rows
            .Select(r => AccountGuard.EnforceSingle(MapRow(r), accountId, t => t.AccountId))
            .ToList();
    }

    private static TradeEntity MapRow(TradeRow r) => new()
    {
        Id = r.Id,
        AccountId = r.AccountId,
        Instrument = new InstrumentRef(r.Instrument),
        Side = (TradeSide)r.Side,
        Quantity = r.Quantity,
        Price = new MoneyValue(r.PriceAmount, new Currency(r.PriceCurrency)),
        Fees = new MoneyValue(r.FeesAmount, new Currency(r.FeesCurrency)),
        ExecutedAt = new DateTimeOffset(DateTime.SpecifyKind(r.ExecutedAt, DateTimeKind.Utc)),
        BrokerKey = r.BrokerKey,
        BrokerReference = r.BrokerReference,
        Name = r.Name,
    };
}
