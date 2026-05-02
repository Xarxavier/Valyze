using Valyze.Domain.Entities.Portfolio;

namespace Valyze.Domain.QueryService;

public interface IPortfolioQueryService
{
    Task<PortfolioViewEntity> GetViewAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<int> CountTradesAsync(Guid accountId, CancellationToken cancellationToken = default);
}
