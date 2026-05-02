using Valyze.Domain.Entities.Portfolio;

namespace Valyze.Domain.Repository;

public interface ITradeRepository
{
    Task<TradeEntity> CreateAsync(TradeEntity trade, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TradeEntity>> CreateManyAsync(
        IEnumerable<TradeEntity> trades,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subset of <paramref name="references"/> that already exist for the
    /// given account and broker. Used by ingestion use cases to skip duplicate trades
    /// before INSERT, avoiding unique-index violations.
    /// </summary>
    Task<IReadOnlySet<string>> FindExistingReferencesAsync(
        Guid accountId,
        string brokerKey,
        IReadOnlyCollection<string> references,
        CancellationToken cancellationToken = default);
}
