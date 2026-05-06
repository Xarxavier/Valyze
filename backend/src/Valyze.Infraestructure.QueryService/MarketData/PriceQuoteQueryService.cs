using Dapper;
using Microsoft.Extensions.Configuration;
using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Money;
using Valyze.Domain.QueryService;
using MoneyValue = Valyze.Domain.Money.Money;

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

    /// <inheritdoc/>
    public async Task<MoneyValue?> GetLatestForSymbolAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();

        // Pick the single most-recently-fetched row for this symbol, across
        // all currencies. DISTINCT ON guarantees we get exactly one row.
        const string sql = @"
            SELECT
                symbol      AS Symbol,
                currency    AS Currency,
                amount      AS Amount,
                source      AS Source,
                fetched_at  AS FetchedAt
            FROM price_quotes
            WHERE UPPER(symbol) = UPPER(@Symbol)
            ORDER BY fetched_at DESC
            LIMIT 1;";

        var row = await connection.QueryFirstOrDefaultAsync<QuoteRow>(sql, new
        {
            Symbol = symbol,
        });

        if (row is null) return null;
        return new MoneyValue(row.Amount, new Currency(row.Currency));
    }
}
