using Valyze.Domain.Entities.Portfolio;

namespace Valyze.Domain.Application.Portfolio;

public interface IGetPortfolioUseCase
{
    Task<PortfolioViewEntity> ExecuteAsync(Guid accountId, CancellationToken cancellationToken = default);
}
