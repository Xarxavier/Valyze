using Microsoft.Extensions.Options;
using Valyze.Domain.Application.Decisions;
using Valyze.Domain.Decisions;
using Valyze.Domain.Enum;
using Valyze.Domain.Exceptions;
using Valyze.Domain.Money;
using Valyze.Domain.QueryService;
using Valyze.Domain.Repository;

namespace Valyze.Application.Decisions;

public class EvaluateDecisionUseCase : IEvaluateDecisionUseCase
{
    private readonly IInvestmentDecisionRepository _repository;
    private readonly IPriceQuoteQueryService _priceQuoteQueryService;
    private readonly DecisionEvaluationOptions _options;

    // Consider a quote fresh if within 24 hours.
    private static readonly TimeSpan PriceFreshnessWindow = TimeSpan.FromHours(24);

    public EvaluateDecisionUseCase(
        IInvestmentDecisionRepository repository,
        IPriceQuoteQueryService priceQuoteQueryService,
        IOptions<DecisionEvaluationOptions> options)
    {
        _repository = repository;
        _priceQuoteQueryService = priceQuoteQueryService;
        _options = options.Value;
    }

    public async Task<DecisionEvaluation> ExecuteAsync(
        Guid decisionId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var decision = await _repository.GetByIdForAccountAsync(decisionId, accountId, cancellationToken);
        if (decision is null)
            throw new BusinessException("msnDecisionNotFound");

        var daysElapsed = (int)(DateTimeOffset.UtcNow - decision.CreatedAt).TotalDays;
        var horizon = decision.EvaluationHorizonDays;

        // AD-3: HOLD without instrument → NOT_APPLICABLE (checked before null-price guard)
        if (decision.Action == DecisionAction.Hold && string.IsNullOrWhiteSpace(decision.Isin))
        {
            return new DecisionEvaluation(
                Status: DecisionStatus.NotApplicable,
                ReturnPercent: null,
                DaysElapsed: daysElapsed,
                Horizon: horizon,
                PriceThen: decision.PriceAtDecision,
                PriceNow: null,
                Message: "instrument-less HOLD — instrument-level evaluation not applicable");
        }

        // REBALANCE without instrument → MIXED (single-leg approximation v1, checked before null-price guard)
        if (decision.Action == DecisionAction.Rebalance && string.IsNullOrWhiteSpace(decision.Isin))
        {
            return new DecisionEvaluation(
                Status: DecisionStatus.Mixed,
                ReturnPercent: null,
                DaysElapsed: daysElapsed,
                Horizon: horizon,
                PriceThen: decision.PriceAtDecision,
                PriceNow: null,
                Message: "instrument-less REBALANCE — single-leg approximation, full evaluation unavailable");
        }

        // AD-4: PriceAtDecision is null → NOT_APPLICABLE (for all other action/instrument combos)
        if (decision.PriceAtDecision is null)
        {
            return new DecisionEvaluation(
                Status: DecisionStatus.NotApplicable,
                ReturnPercent: null,
                DaysElapsed: daysElapsed,
                Horizon: horizon,
                PriceThen: null,
                PriceNow: null,
                Message: "price unavailable at decision time");
        }

        // Still within the horizon window
        if (daysElapsed < horizon)
        {
            // Try to get current price for display, but status is always PENDING_HORIZON
            Money? currentPrice = null;
            if (!string.IsNullOrWhiteSpace(decision.Isin))
            {
                var freshSince = DateTimeOffset.UtcNow - PriceFreshnessWindow;
                var quotes = await _priceQuoteQueryService.GetFreshAsync(
                    [decision.Isin],
                    decision.PriceAtDecision.Value.Currency,
                    freshSince,
                    cancellationToken);
                if (quotes.Count > 0)
                    currentPrice = new Money(quotes[0].Amount, quotes[0].Currency);
            }

            return new DecisionEvaluation(
                Status: DecisionStatus.PendingHorizon,
                ReturnPercent: null,
                DaysElapsed: daysElapsed,
                Horizon: horizon,
                PriceThen: decision.PriceAtDecision,
                PriceNow: currentPrice,
                Message: null);
        }

        // Past horizon — fetch current price
        if (string.IsNullOrWhiteSpace(decision.Isin))
        {
            // Should not happen for BUY/SELL, but guard defensively
            return new DecisionEvaluation(
                Status: DecisionStatus.NotApplicable,
                ReturnPercent: null,
                DaysElapsed: daysElapsed,
                Horizon: horizon,
                PriceThen: decision.PriceAtDecision,
                PriceNow: null,
                Message: "no instrument — price evaluation unavailable");
        }

        var freshSinceEval = DateTimeOffset.UtcNow - PriceFreshnessWindow;
        var currentQuotes = await _priceQuoteQueryService.GetFreshAsync(
            [decision.Isin],
            decision.PriceAtDecision.Value.Currency,
            freshSinceEval,
            cancellationToken);

        if (currentQuotes.Count == 0)
        {
            return new DecisionEvaluation(
                Status: DecisionStatus.NotApplicable,
                ReturnPercent: null,
                DaysElapsed: daysElapsed,
                Horizon: horizon,
                PriceThen: decision.PriceAtDecision,
                PriceNow: null,
                Message: "no current quote available for evaluation");
        }

        var priceNow = new Money(currentQuotes[0].Amount, currentQuotes[0].Currency);
        var priceThen = decision.PriceAtDecision.Value;

        // Round to 2 decimal places for display
        var returnPct = Math.Round((priceNow.Amount - priceThen.Amount) / priceThen.Amount * 100m, 2);

        var threshold = _options.AchievementThreshold * 100m; // convert 0.05 → 5.0

        DecisionStatus status;
        if (decision.Action == DecisionAction.Rebalance)
        {
            // Single-leg REBALANCE with instrument — MIXED v1 approximation
            status = DecisionStatus.Mixed;
        }
        else if (decision.Action == DecisionAction.Sell)
        {
            // Favorable for SELL = price dropped beyond threshold
            status = returnPct <= -threshold ? DecisionStatus.Achieved : DecisionStatus.Underperforming;
        }
        else
        {
            // BUY / HOLD with instrument: unfavorable = returnPct <= -threshold
            status = returnPct <= -threshold ? DecisionStatus.Underperforming : DecisionStatus.Achieved;
        }

        return new DecisionEvaluation(
            Status: status,
            ReturnPercent: returnPct,
            DaysElapsed: daysElapsed,
            Horizon: horizon,
            PriceThen: priceThen,
            PriceNow: priceNow,
            Message: null);
    }
}
