using Shouldly;
using Valyze.Domain.Enum;
using Xunit;

namespace Valyze.Domain.Tests.Decisions;

/// <summary>
/// Pins all Decision enum numeric values so accidental reordering is caught immediately.
/// </summary>
public sealed class EnumStabilityTests
{
    [Fact]
    public void DecisionAction_values_are_stable()
    {
        ((int)DecisionAction.Buy).ShouldBe(1);
        ((int)DecisionAction.Sell).ShouldBe(2);
        ((int)DecisionAction.Hold).ShouldBe(3);
        ((int)DecisionAction.Rebalance).ShouldBe(4);
    }

    [Fact]
    public void DecisionSource_values_are_stable()
    {
        ((int)DecisionSource.AiRecommendation).ShouldBe(1);
        ((int)DecisionSource.UserOwnAnalysis).ShouldBe(2);
        ((int)DecisionSource.ExternalNews).ShouldBe(3);
        ((int)DecisionSource.ThirdPartyTip).ShouldBe(4);
        ((int)DecisionSource.Other).ShouldBe(5);
    }

    [Fact]
    public void QuantityUnits_values_are_stable()
    {
        ((int)QuantityUnits.Shares).ShouldBe(1);
        ((int)QuantityUnits.AmountBaseCcy).ShouldBe(2);
        ((int)QuantityUnits.PercentPortfolio).ShouldBe(3);
    }

    [Fact]
    public void DecisionStatus_values_are_stable()
    {
        ((int)DecisionStatus.PendingHorizon).ShouldBe(1);
        ((int)DecisionStatus.Achieved).ShouldBe(2);
        ((int)DecisionStatus.Underperforming).ShouldBe(3);
        ((int)DecisionStatus.Mixed).ShouldBe(4);
        ((int)DecisionStatus.NotApplicable).ShouldBe(5);
    }
}
