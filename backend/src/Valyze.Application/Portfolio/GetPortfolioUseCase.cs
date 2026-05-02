using Valyze.Domain.Application.Portfolio;
using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Exceptions;
using Valyze.Domain.QueryService;

namespace Valyze.Application.Portfolio;

public class GetPortfolioUseCase : IGetPortfolioUseCase
{
    private readonly IPortfolioQueryService _portfolioQueryService;

    public GetPortfolioUseCase(IPortfolioQueryService portfolioQueryService)
    {
        _portfolioQueryService = portfolioQueryService;
    }

    public async Task<PortfolioViewEntity> ExecuteAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            throw new BusinessException("msnAccountIdRequired");

        return await _portfolioQueryService.GetViewAsync(accountId, cancellationToken);
    }
}
