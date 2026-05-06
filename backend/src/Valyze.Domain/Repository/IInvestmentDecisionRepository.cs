using Valyze.Domain.Entities.Decisions;

namespace Valyze.Domain.Repository;

public interface IInvestmentDecisionRepository
{
    /// <summary>Persists a new decision and returns its generated Id.</summary>
    Task<Guid> CreateAsync(
        InvestmentDecisionEntity decision,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets (or clears) the linked_trade_id for the decision identified by
    /// (decisionId, accountId). Throws BusinessException if not found or if
    /// the referenced trade does not belong to the same account.
    /// </summary>
    Task UpdateLinkedTradeAsync(
        Guid decisionId,
        Guid accountId,
        Guid? tradeId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the decision for the given account, or null if not found.</summary>
    Task<InvestmentDecisionEntity?> GetByIdForAccountAsync(
        Guid decisionId,
        Guid accountId,
        CancellationToken cancellationToken = default);
}
