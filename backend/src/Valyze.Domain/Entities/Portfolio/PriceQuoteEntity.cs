using Valyze.Domain.Money;

namespace Valyze.Domain.Entities.Portfolio;

/// <summary>
/// Cached spot price for a single instrument symbol in a single currency.
/// Composite identity is (Symbol, Currency); a fresh fetch upserts the
/// existing row rather than appending history. Source records which feed
/// provided it (e.g. "coingecko") for traceability.
/// </summary>
public sealed class PriceQuoteEntity
{
    public string Symbol { get; set; } = null!;
    public Currency Currency { get; set; }
    public decimal Amount { get; set; }
    public string Source { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
}
