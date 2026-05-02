using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Money;

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
}
