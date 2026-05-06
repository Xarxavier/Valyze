using Valyze.Domain.Application.Decisions;
using Valyze.Domain.Decisions;
using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Enum;

namespace Valyze.Domain.QueryService;

public interface IInvestmentDecisionQueryService
{
    /// <summary>
    /// Returns decisions for the given account matching the optional filters,
    /// ordered most-recent first. Post-validates with AccountGuard.EnforceMany.
    /// </summary>
    Task<IReadOnlyList<InvestmentDecisionEntity>> ListByAccountAsync(
        Guid accountId,
        ListDecisionsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns per-source aggregated track-record rows for the given account.
    /// Passes @AccountId, @SourceFilter, and @Threshold as Dapper parameters.
    /// Post-validates with AccountGuard.EnforceMany.
    /// </summary>
    Task<IReadOnlyList<DecisionTrackRecordRow>> GetTrackRecordAsync(
        Guid accountId,
        DecisionSource? sourceFilter,
        decimal achievementThreshold,
        CancellationToken cancellationToken = default);
}
