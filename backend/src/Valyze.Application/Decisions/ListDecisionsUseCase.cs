using Valyze.Domain.Application.Decisions;
using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Exceptions;
using Valyze.Domain.QueryService;

namespace Valyze.Application.Decisions;

public class ListDecisionsUseCase : IListDecisionsUseCase
{
    private readonly IInvestmentDecisionQueryService _queryService;

    public ListDecisionsUseCase(IInvestmentDecisionQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<IReadOnlyList<InvestmentDecisionEntity>> ExecuteAsync(
        ListDecisionsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.AccountId == Guid.Empty)
            throw new BusinessException("msnDecisionAccountIdRequired");

        return await _queryService.ListByAccountAsync(query.AccountId, query, cancellationToken);
    }
}
