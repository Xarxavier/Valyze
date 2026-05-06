using Shouldly;
using Valyze.Domain.Decisions;
using Valyze.Domain.Enum;
using MoneyValue = Valyze.Domain.Money.Money;
using Xunit;

namespace Valyze.Domain.Tests.Decisions;

public sealed class DecisionEvaluationTests
{
    [Fact]
    public void Can_construct_with_PendingHorizon_status()
    {
        var eval = new DecisionEvaluation(
            Status: DecisionStatus.PendingHorizon,
            ReturnPercent: null,
            DaysElapsed: 30,
            Horizon: 180,
            PriceThen: new MoneyValue(100m, Valyze.Domain.Money.Currency.Eur),
            PriceNow: new MoneyValue(105m, Valyze.Domain.Money.Currency.Eur),
            Message: null);

        eval.Status.ShouldBe(DecisionStatus.PendingHorizon);
        eval.ReturnPercent.ShouldBeNull();
    }

    [Fact]
    public void Can_construct_with_Achieved_status_and_return_percent()
    {
        var eval = new DecisionEvaluation(
            Status: DecisionStatus.Achieved,
            ReturnPercent: 10.0m,
            DaysElapsed: 200,
            Horizon: 180,
            PriceThen: new MoneyValue(100m, Valyze.Domain.Money.Currency.Eur),
            PriceNow: new MoneyValue(110m, Valyze.Domain.Money.Currency.Eur),
            Message: null);

        eval.Status.ShouldBe(DecisionStatus.Achieved);
        eval.ReturnPercent.ShouldBe(10.0m);
    }

    [Fact]
    public void NotApplicable_status_has_null_return_percent()
    {
        var eval = new DecisionEvaluation(
            Status: DecisionStatus.NotApplicable,
            ReturnPercent: null,
            DaysElapsed: 100,
            Horizon: 90,
            PriceThen: null,
            PriceNow: null,
            Message: "price unavailable at decision time");

        eval.Status.ShouldBe(DecisionStatus.NotApplicable);
        eval.ReturnPercent.ShouldBeNull();
        eval.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Can_construct_with_null_prices()
    {
        var eval = new DecisionEvaluation(
            Status: DecisionStatus.NotApplicable,
            ReturnPercent: null,
            DaysElapsed: 10,
            Horizon: 90,
            PriceThen: null,
            PriceNow: null,
            Message: "instrument-less HOLD");

        eval.PriceThen.ShouldBeNull();
        eval.PriceNow.ShouldBeNull();
    }

    [Fact]
    public void Mixed_status_can_carry_return_percent()
    {
        var eval = new DecisionEvaluation(
            Status: DecisionStatus.Mixed,
            ReturnPercent: 3.5m,
            DaysElapsed: 100,
            Horizon: 90,
            PriceThen: new MoneyValue(100m, Valyze.Domain.Money.Currency.Usd),
            PriceNow: new MoneyValue(103.5m, Valyze.Domain.Money.Currency.Usd),
            Message: "single-leg approximation");

        eval.Status.ShouldBe(DecisionStatus.Mixed);
        eval.ReturnPercent.ShouldBe(3.5m);
    }
}
