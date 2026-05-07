using Valyze.Domain.Application.Decisions;
using Valyze.Domain.Exceptions;
using Valyze.Domain.Repository;

namespace Valyze.Application.Decisions;

public class LinkDecisionToTradeUseCase : ILinkDecisionToTradeUseCase
{
    private readonly IInvestmentDecisionRepository _repository;

    public LinkDecisionToTradeUseCase(IInvestmentDecisionRepository repository)
    {
        _repository = repository;
    }

    public async Task ExecuteAsync(
        Guid decisionId,
        Guid accountId,
        Guid? tradeId,
        CancellationToken cancellationToken = default)
    {
        // Verify the decision exists and belongs to this account (prevents existence leak)
        var decision = await _repository.GetByIdForAccountAsync(decisionId, accountId, cancellationToken);
        if (decision is null)
            throw new BusinessException("msnDecisionNotFound");

        // Delegate tenant guard for the trade to the repository layer (AD-6, design R9)
        await _repository.UpdateLinkedTradeAsync(decisionId, accountId, tradeId, cancellationToken);
    }
}
