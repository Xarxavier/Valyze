using Valyze.Domain.Entities.Portfolio;

namespace Valyze.Domain.QueryService;

public interface ITradeQueryService
{
    Task<IReadOnlyList<TradeEntity>> ListByAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}
