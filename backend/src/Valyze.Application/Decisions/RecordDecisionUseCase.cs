using Valyze.Domain.Application.Decisions;
using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Enum;
using Valyze.Domain.Exceptions;
using Valyze.Domain.Money;
using Valyze.Domain.QueryService;
using Valyze.Domain.Repository;

namespace Valyze.Application.Decisions;

public class RecordDecisionUseCase : IRecordDecisionUseCase
{
    private readonly IInvestmentDecisionRepository _repository;
    private readonly IPriceQuoteQueryService _priceQuoteQueryService;

    // Price freshness window: quotes up to 24 hours old are acceptable for the snapshot.
    private static readonly TimeSpan PriceFreshnessWindow = TimeSpan.FromHours(24);

    public RecordDecisionUseCase(
        IInvestmentDecisionRepository repository,
        IPriceQuoteQueryService priceQuoteQueryService)
    {
        _repository = repository;
        _priceQuoteQueryService = priceQuoteQueryService;
    }

    public async Task<RecordDecisionResult> ExecuteAsync(
        RecordDecisionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.AccountId == Guid.Empty)
            throw new BusinessException("msnDecisionAccountIdRequired");

        if (string.IsNullOrWhiteSpace(command.Rationale))
            throw new BusinessException("msnDecisionRationaleRequired");

        var horizonDays = command.EvaluationHorizonDays ?? ResolveDefaultHorizon(command.Action);

        // Attempt to snapshot the current price. Fail-soft per AD-4.
        Money? priceAtDecision = null;
        string? warning = null;

        if (!string.IsNullOrWhiteSpace(command.Isin))
        {
            var freshSince = DateTimeOffset.UtcNow - PriceFreshnessWindow;
            // We request any currency — just get what's cached for this symbol.
            // The first result in any currency is used; currency mismatch across
            // evaluation is caught later by the Money VO invariant.
            var quotes = await _priceQuoteQueryService.GetFreshAsync(
                [command.Isin],
                Currency.Eur, // default request currency; real impl queries by symbol only
                freshSince,
                cancellationToken);

            if (quotes.Count > 0)
            {
                var q = quotes[0];
                priceAtDecision = new Money(q.Amount, q.Currency);
            }
            else
            {
                warning = "Price snapshot unavailable at decision time; snapshot stored as NULL.";
            }
        }

        var entity = new InvestmentDecisionEntity
        {
            Id = Guid.NewGuid(),
            AccountId = command.AccountId,
            Source = command.Source,
            Action = command.Action,
            Isin = command.Isin,
            Ticker = command.Ticker,
            QuantityAmount = command.QuantityAmount,
            QuantityCurrency = string.IsNullOrWhiteSpace(command.QuantityCurrency)
                ? null
                : new Currency(command.QuantityCurrency),
            QuantityUnits = command.QuantityUnits,
            PriceAtDecision = priceAtDecision,
            Rationale = command.Rationale,
            EvaluationHorizonDays = horizonDays,
            SourceOtherNote = command.SourceOtherNote,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var id = await _repository.CreateAsync(entity, cancellationToken);

        return new RecordDecisionResult(id, warning);
    }

    internal static int ResolveDefaultHorizon(DecisionAction action) => action switch
    {
        DecisionAction.Buy => 180,
        DecisionAction.Sell => 30,
        DecisionAction.Hold => 90,
        DecisionAction.Rebalance => 90,
        _ => 180,
    };
}
