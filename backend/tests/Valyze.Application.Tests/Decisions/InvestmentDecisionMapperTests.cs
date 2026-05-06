using Shouldly;
using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Enum;
using Valyze.Domain.Money;
using Valyze.Infraestructure.EntityFramework.Mapper;
using Xunit;

namespace Valyze.Application.Tests.Decisions;

/// <summary>
/// Round-trip tests: Domain → EF → Domain.
/// Validates that InvestmentDecisionMapper preserves all fields,
/// including nullable Money pairs and nullable FKs.
/// </summary>
public sealed class InvestmentDecisionMapperTests
{
    private static readonly Guid SampleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SampleAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SampleTradeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SampleChatId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset SampleDate = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void RoundTrip_preserves_all_non_nullable_fields()
    {
        var domain = new InvestmentDecisionEntity
        {
            Id = SampleId,
            AccountId = SampleAccountId,
            Source = DecisionSource.UserOwnAnalysis,
            Action = DecisionAction.Buy,
            Isin = "IE00B4L5Y983",
            Ticker = "IWDA",
            QuantityAmount = 10.5m,
            QuantityCurrency = null,
            QuantityUnits = QuantityUnits.Shares,
            PriceAtDecision = new Money(100.50m, Currency.Eur),
            Rationale = "Strong fundamentals, buy on dip.",
            EvaluationHorizonDays = 180,
            AiChatSessionId = null,
            LinkedTradeId = null,
            SourceOtherNote = null,
            CreatedAt = SampleDate,
            UpdatedAt = SampleDate.AddDays(1),
        };

        var ef = InvestmentDecisionMapper.ToEf(domain);
        var roundTripped = InvestmentDecisionMapper.ToDomain(ef);

        roundTripped.Id.ShouldBe(domain.Id);
        roundTripped.AccountId.ShouldBe(domain.AccountId);
        roundTripped.Source.ShouldBe(domain.Source);
        roundTripped.Action.ShouldBe(domain.Action);
        roundTripped.Isin.ShouldBe(domain.Isin);
        roundTripped.Ticker.ShouldBe(domain.Ticker);
        roundTripped.QuantityAmount.ShouldBe(domain.QuantityAmount);
        roundTripped.QuantityCurrency.ShouldBeNull();
        roundTripped.QuantityUnits.ShouldBe(domain.QuantityUnits);
        roundTripped.PriceAtDecision.ShouldBe(domain.PriceAtDecision);
        roundTripped.Rationale.ShouldBe(domain.Rationale);
        roundTripped.EvaluationHorizonDays.ShouldBe(domain.EvaluationHorizonDays);
        roundTripped.AiChatSessionId.ShouldBeNull();
        roundTripped.LinkedTradeId.ShouldBeNull();
        roundTripped.SourceOtherNote.ShouldBeNull();
        roundTripped.CreatedAt.ShouldBe(domain.CreatedAt);
        roundTripped.UpdatedAt.ShouldBe(domain.UpdatedAt);
    }

    [Fact]
    public void RoundTrip_preserves_null_price_pair()
    {
        // When price feed was unavailable — both amount and currency are null
        var domain = new InvestmentDecisionEntity
        {
            Id = SampleId,
            AccountId = SampleAccountId,
            Source = DecisionSource.AiRecommendation,
            Action = DecisionAction.Sell,
            Isin = "US0378331005",
            Ticker = null,
            QuantityAmount = null,
            QuantityCurrency = null,
            QuantityUnits = QuantityUnits.PercentPortfolio,
            PriceAtDecision = null, // price feed unavailable
            Rationale = "Macro risk rising.",
            EvaluationHorizonDays = 30,
            AiChatSessionId = null,
            LinkedTradeId = null,
            SourceOtherNote = null,
            CreatedAt = SampleDate,
            UpdatedAt = SampleDate,
        };

        var ef = InvestmentDecisionMapper.ToEf(domain);
        var roundTripped = InvestmentDecisionMapper.ToDomain(ef);

        // EF columns must both be null
        ef.PriceAtDecisionAmount.ShouldBeNull();
        ef.PriceAtDecisionCurrency.ShouldBeNull();

        // Domain round-trip must also be null
        roundTripped.PriceAtDecision.ShouldBeNull();
    }

    [Fact]
    public void RoundTrip_preserves_nullable_linked_trade_id()
    {
        var domain = new InvestmentDecisionEntity
        {
            Id = SampleId,
            AccountId = SampleAccountId,
            Source = DecisionSource.ExternalNews,
            Action = DecisionAction.Hold,
            Isin = "IE00B4L5Y983",
            Ticker = null,
            QuantityAmount = null,
            QuantityCurrency = null,
            QuantityUnits = QuantityUnits.Shares,
            PriceAtDecision = new Money(95m, new Currency("USD")),
            Rationale = "Hold — still within target range.",
            EvaluationHorizonDays = 90,
            AiChatSessionId = SampleChatId,
            LinkedTradeId = SampleTradeId, // set
            SourceOtherNote = null,
            CreatedAt = SampleDate,
            UpdatedAt = SampleDate,
        };

        var ef = InvestmentDecisionMapper.ToEf(domain);
        var roundTripped = InvestmentDecisionMapper.ToDomain(ef);

        ef.LinkedTradeId.ShouldBe(SampleTradeId);
        roundTripped.LinkedTradeId.ShouldBe(SampleTradeId);
        roundTripped.AiChatSessionId.ShouldBe(SampleChatId);
    }

    [Fact]
    public void RoundTrip_preserves_quantity_currency_when_units_are_AmountBaseCcy()
    {
        var domain = new InvestmentDecisionEntity
        {
            Id = SampleId,
            AccountId = SampleAccountId,
            Source = DecisionSource.ThirdPartyTip,
            Action = DecisionAction.Rebalance,
            Isin = null, // REBALANCE can be instrument-less
            Ticker = null,
            QuantityAmount = 5000m,
            QuantityCurrency = new Currency("EUR"),
            QuantityUnits = QuantityUnits.AmountBaseCcy,
            PriceAtDecision = null,
            Rationale = "Rebalance 5000 EUR into bonds.",
            EvaluationHorizonDays = 90,
            AiChatSessionId = null,
            LinkedTradeId = null,
            SourceOtherNote = null,
            CreatedAt = SampleDate,
            UpdatedAt = SampleDate,
        };

        var ef = InvestmentDecisionMapper.ToEf(domain);
        var roundTripped = InvestmentDecisionMapper.ToDomain(ef);

        ef.QuantityCurrency.ShouldBe("EUR");
        roundTripped.QuantityCurrency.ShouldNotBeNull();
        roundTripped.QuantityCurrency!.Value.Code.ShouldBe("EUR");
        roundTripped.QuantityAmount.ShouldBe(5000m);
    }

    [Fact]
    public void RoundTrip_preserves_SourceOtherNote_for_OTHER_source()
    {
        var domain = new InvestmentDecisionEntity
        {
            Id = SampleId,
            AccountId = SampleAccountId,
            Source = DecisionSource.Other,
            Action = DecisionAction.Buy,
            Isin = "FR0000131104",
            Ticker = null,
            QuantityAmount = 50m,
            QuantityCurrency = null,
            QuantityUnits = QuantityUnits.Shares,
            PriceAtDecision = new Money(25.30m, new Currency("EUR")),
            Rationale = "Friend recommended it.",
            EvaluationHorizonDays = 180,
            AiChatSessionId = null,
            LinkedTradeId = null,
            SourceOtherNote = "Tip from Javier at the conference.",
            CreatedAt = SampleDate,
            UpdatedAt = SampleDate,
        };

        var ef = InvestmentDecisionMapper.ToEf(domain);
        var roundTripped = InvestmentDecisionMapper.ToDomain(ef);

        ef.SourceOtherNote.ShouldBe("Tip from Javier at the conference.");
        roundTripped.SourceOtherNote.ShouldBe("Tip from Javier at the conference.");
        roundTripped.Source.ShouldBe(DecisionSource.Other);
    }

    [Fact]
    public void Enum_values_are_persisted_as_expected_short_values()
    {
        // Regression guard: enum → short cast must match design (Action: Buy=1, Sell=2, Hold=3, Rebalance=4)
        var domain = new InvestmentDecisionEntity
        {
            Id = SampleId,
            AccountId = SampleAccountId,
            Source = DecisionSource.AiRecommendation,
            Action = DecisionAction.Rebalance,
            Isin = null,
            QuantityUnits = QuantityUnits.PercentPortfolio,
            PriceAtDecision = null,
            Rationale = "Rebalance portfolio.",
            EvaluationHorizonDays = 90,
            CreatedAt = SampleDate,
            UpdatedAt = SampleDate,
        };

        var ef = InvestmentDecisionMapper.ToEf(domain);

        ef.Source.ShouldBe((short)1); // AiRecommendation = 1
        ef.Action.ShouldBe((short)4); // Rebalance = 4
        ef.QuantityUnits.ShouldBe((short)3); // PercentPortfolio = 3
    }
}
