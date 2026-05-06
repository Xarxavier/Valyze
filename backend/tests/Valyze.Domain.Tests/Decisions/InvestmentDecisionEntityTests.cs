using Shouldly;
using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Enum;
using MoneyValue = Valyze.Domain.Money.Money;
using Xunit;

namespace Valyze.Domain.Tests.Decisions;

/// <summary>
/// Documents invariant contracts for InvestmentDecisionEntity.
/// The entity itself is a POCO; invariants are enforced at the use case boundary.
/// These tests verify the POCO can be constructed with expected field assignments.
/// </summary>
public sealed class InvestmentDecisionEntityTests
{
    [Fact]
    public void Entity_can_be_constructed_with_all_required_fields()
    {
        var id = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var entity = new InvestmentDecisionEntity
        {
            Id = id,
            AccountId = accountId,
            Source = DecisionSource.AiRecommendation,
            Action = DecisionAction.Buy,
            QuantityUnits = QuantityUnits.Shares,
            Rationale = "AI suggested this ETF",
            EvaluationHorizonDays = 180,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        entity.Id.ShouldBe(id);
        entity.AccountId.ShouldBe(accountId);
        entity.Source.ShouldBe(DecisionSource.AiRecommendation);
        entity.Action.ShouldBe(DecisionAction.Buy);
        entity.PriceAtDecision.ShouldBeNull();
        entity.LinkedTradeId.ShouldBeNull();
        entity.Isin.ShouldBeNull();
    }

    [Fact]
    public void PriceAtDecision_snapshot_can_be_null()
    {
        var entity = BuildMinimal();
        entity.PriceAtDecision = null;
        entity.PriceAtDecision.ShouldBeNull();
    }

    [Fact]
    public void PriceAtDecision_snapshot_can_be_set_as_Money()
    {
        var entity = BuildMinimal();
        entity.PriceAtDecision = new MoneyValue(100m, Valyze.Domain.Money.Currency.Eur);
        entity.PriceAtDecision.ShouldNotBeNull();
        entity.PriceAtDecision!.Value.Amount.ShouldBe(100m);
        entity.PriceAtDecision!.Value.Currency.ShouldBe(Valyze.Domain.Money.Currency.Eur);
    }

    [Fact]
    public void Source_OTHER_allows_SourceOtherNote()
    {
        var entity = BuildMinimal();
        entity.Source = DecisionSource.Other;
        entity.SourceOtherNote = "Friend on Twitter";
        entity.SourceOtherNote.ShouldBe("Friend on Twitter");
    }

    [Fact]
    public void LinkedTradeId_can_be_set_and_cleared()
    {
        var entity = BuildMinimal();
        var tradeId = Guid.NewGuid();
        entity.LinkedTradeId = tradeId;
        entity.LinkedTradeId.ShouldBe(tradeId);
        entity.LinkedTradeId = null;
        entity.LinkedTradeId.ShouldBeNull();
    }

    private static InvestmentDecisionEntity BuildMinimal() => new()
    {
        Id = Guid.NewGuid(),
        AccountId = Guid.NewGuid(),
        Source = DecisionSource.UserOwnAnalysis,
        Action = DecisionAction.Buy,
        QuantityUnits = QuantityUnits.Shares,
        Rationale = "Test rationale",
        EvaluationHorizonDays = 180,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
