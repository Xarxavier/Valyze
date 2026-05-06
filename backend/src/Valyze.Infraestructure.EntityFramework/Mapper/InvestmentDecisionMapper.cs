using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Enum;
using Valyze.Domain.Money;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Infraestructure.EntityFramework.Mapper;

public static class InvestmentDecisionMapper
{
    public static Entities.InvestmentDecision ToEf(InvestmentDecisionEntity domain) => new()
    {
        Id = domain.Id,
        AccountId = domain.AccountId,
        Source = (short)domain.Source,
        Action = (short)domain.Action,
        Isin = domain.Isin,
        Ticker = domain.Ticker,
        QuantityAmount = domain.QuantityAmount,
        QuantityCurrency = domain.QuantityCurrency?.Code,
        QuantityUnits = (short)domain.QuantityUnits,
        PriceAtDecisionAmount = domain.PriceAtDecision?.Amount,
        PriceAtDecisionCurrency = domain.PriceAtDecision?.Currency.Code,
        Rationale = domain.Rationale,
        EvaluationHorizonDays = domain.EvaluationHorizonDays,
        AiChatSessionId = domain.AiChatSessionId,
        LinkedTradeId = domain.LinkedTradeId,
        SourceOtherNote = domain.SourceOtherNote,
        CreatedAt = domain.CreatedAt,
        UpdatedAt = domain.UpdatedAt,
    };

    public static InvestmentDecisionEntity ToDomain(Entities.InvestmentDecision ef) => new()
    {
        Id = ef.Id,
        AccountId = ef.AccountId,
        Source = (DecisionSource)ef.Source,
        Action = (DecisionAction)ef.Action,
        Isin = ef.Isin,
        Ticker = ef.Ticker,
        QuantityAmount = ef.QuantityAmount,
        QuantityCurrency = ef.QuantityCurrency is not null
            ? new Currency(ef.QuantityCurrency)
            : null,
        QuantityUnits = (QuantityUnits)ef.QuantityUnits,
        PriceAtDecision = ef.PriceAtDecisionAmount.HasValue && ef.PriceAtDecisionCurrency is not null
            ? new MoneyValue(ef.PriceAtDecisionAmount.Value, new Currency(ef.PriceAtDecisionCurrency))
            : null,
        Rationale = ef.Rationale,
        EvaluationHorizonDays = ef.EvaluationHorizonDays,
        AiChatSessionId = ef.AiChatSessionId,
        LinkedTradeId = ef.LinkedTradeId,
        SourceOtherNote = ef.SourceOtherNote,
        CreatedAt = ef.CreatedAt,
        UpdatedAt = ef.UpdatedAt,
    };
}
