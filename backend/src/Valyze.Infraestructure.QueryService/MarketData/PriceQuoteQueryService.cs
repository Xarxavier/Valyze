using Dapper;
using Microsoft.Extensions.Configuration;
using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Money;
using Valyze.Domain.QueryService;

namespace Valyze.Infraestructure.QueryService.MarketData;

public class PriceQuoteQueryService : BaseQueryService, IPriceQuoteQueryService
{
    public PriceQuoteQueryService(IConfiguration configuration) : base(configuration) { }

    private sealed record QuoteRow(
        string Symbol,
        string Currency,
        decimal Amount,
        string Source,
        DateTime FetchedAt);

    public async Task<IReadOnlyList<PriceQuoteEntity>> GetFreshAsync(
        IReadOnlyCollection<string> symbols,
        Currency currency,
        DateTimeOffset freshSince,
        CancellationToken cancellationToken = default)
    {
        if (symbols.Count == 0) return [];

        using var connection = CreateConnection();

        const string sql = @"
            SELECT
                symbol      AS Symbol,
                currency    AS Currency,
                amount      AS Amount,
                source      AS Source,
                fetched_at  AS FetchedAt
            FROM price_quotes
            WHERE currency = @Currency
              AND fetched_at >= @FreshSince
              AND UPPER(symbol) = ANY(@Symbols);";

        var rows = await connection.QueryAsync<QuoteRow>(sql, new
        {
            Currency = currency.Code,
            FreshSince = freshSince.UtcDateTime,
            Symbols = symbols.Select(s => s.ToUpperInvariant()).ToArray(),
        });

        return rows.Select(r => new PriceQuoteEntity
        {
            Symbol = r.Symbol,
            Currency = new Currency(r.Currency),
            Amount = r.Amount,
            Source = r.Source,
            FetchedAt = new DateTimeOffset(DateTime.SpecifyKind(r.FetchedAt, DateTimeKind.Utc)),
        }).ToList();
    }
}
