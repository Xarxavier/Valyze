using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Money;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Domain.QueryService;

public interface IPriceQuoteQueryService
{
    /// <summary>
    /// Returns cached quotes for the requested symbols in the given currency
    /// whose <c>FetchedAt</c> is on or after <paramref name="freshSince"/>.
    /// Stale or missing entries are simply omitted — callers fetch them from
    /// the live feed and upsert via the repository.
    /// </summary>
    Task<IReadOnlyList<PriceQuoteEntity>> GetFreshAsync(
        IReadOnlyCollection<string> symbols,
        Currency currency,
        DateTimeOffset freshSince,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the single most-recently-fetched price for the given symbol
    /// in its native quote currency, regardless of which currency it was
    /// stored in. Returns <c>null</c> when no quote is cached.
    ///
    /// Intended for the public market-price endpoint where the caller does
    /// not know the instrument's native currency in advance.
    /// </summary>
    Task<MoneyValue?> GetLatestForSymbolAsync(
        string symbol,
        CancellationToken cancellationToken = default);
}
