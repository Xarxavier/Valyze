using Valyze.Domain.Entities.Portfolio;

namespace Valyze.Domain.Repository;

public interface IPriceQuoteRepository
{
    /// <summary>
    /// Upserts a batch of quotes. Existing (symbol, currency) rows are updated
    /// in place; missing rows are inserted. Quotes are NOT account-scoped — the
    /// cache is shared across the whole install.
    /// </summary>
    Task UpsertManyAsync(
        IEnumerable<PriceQuoteEntity> quotes,
        CancellationToken cancellationToken = default);
}
