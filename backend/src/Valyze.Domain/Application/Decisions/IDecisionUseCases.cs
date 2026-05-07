using Valyze.Domain.Decisions;
using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Enum;

namespace Valyze.Domain.Application.Decisions;

// ─── Record ───────────────────────────────────────────────────────────────────

public sealed record RecordDecisionCommand(
    Guid AccountId,
    DecisionSource Source,
    DecisionAction Action,
    string? Isin,
    string? Ticker,
    decimal? QuantityAmount,
    string? QuantityCurrency,
    QuantityUnits QuantityUnits,
    string Rationale,
    int? EvaluationHorizonDays,
    string? SourceOtherNote);

public sealed record RecordDecisionResult(Guid Id, string? Warning);

public interface IRecordDecisionUseCase
{
    Task<RecordDecisionResult> ExecuteAsync(
        RecordDecisionCommand command,
        CancellationToken cancellationToken = default);
}

// ─── List ──────────────────────────────────────────────────────────────────────

public sealed record ListDecisionsQuery(
    Guid AccountId,
    int? Limit,
    DateTimeOffset? Since,
    DecisionSource? Source,
    DecisionAction? Action,
    string? Isin);

public interface IListDecisionsUseCase
{
    Task<IReadOnlyList<InvestmentDecisionEntity>> ExecuteAsync(
        ListDecisionsQuery query,
        CancellationToken cancellationToken = default);
}

// ─── Evaluate ─────────────────────────────────────────────────────────────────

public interface IEvaluateDecisionUseCase
{
    Task<DecisionEvaluation> ExecuteAsync(
        Guid decisionId,
        Guid accountId,
        CancellationToken cancellationToken = default);
}

// ─── Track record ─────────────────────────────────────────────────────────────

public interface IGetDecisionTrackRecordUseCase
{
    Task<DecisionTrackRecord> ExecuteAsync(
        Guid accountId,
        DecisionSource? sourceFilter,
        CancellationToken cancellationToken = default);
}

// ─── Link to trade ────────────────────────────────────────────────────────────

public interface ILinkDecisionToTradeUseCase
{
    Task ExecuteAsync(
        Guid decisionId,
        Guid accountId,
        Guid? tradeId,
        CancellationToken cancellationToken = default);
}
