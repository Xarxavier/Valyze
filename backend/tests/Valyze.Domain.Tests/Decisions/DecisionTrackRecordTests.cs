using Shouldly;
using Valyze.Domain.Decisions;
using Valyze.Domain.Enum;
using Xunit;

namespace Valyze.Domain.Tests.Decisions;

public sealed class DecisionTrackRecordTests
{
    [Fact]
    public void Can_construct_with_multiple_rows()
    {
        var rows = new[]
        {
            new DecisionTrackRecordRow(
                Source: DecisionSource.AiRecommendation,
                Total: 4,
                Achieved: 3,
                Underperforming: 1,
                Pending: 0,
                NotApplicable: 0,
                Mixed: 0,
                AvgReturnPercent: 8.5m),
            new DecisionTrackRecordRow(
                Source: DecisionSource.UserOwnAnalysis,
                Total: 2,
                Achieved: 1,
                Underperforming: 0,
                Pending: 1,
                NotApplicable: 0,
                Mixed: 0,
                AvgReturnPercent: null),
        };

        var record = new DecisionTrackRecord(rows);

        record.BySource.Count.ShouldBe(2);
        record.BySource[0].Source.ShouldBe(DecisionSource.AiRecommendation);
        record.BySource[1].Source.ShouldBe(DecisionSource.UserOwnAnalysis);
    }

    [Fact]
    public void AvgReturnPercent_is_null_when_no_resolved_decisions()
    {
        var rows = new[]
        {
            new DecisionTrackRecordRow(
                Source: DecisionSource.ExternalNews,
                Total: 3,
                Achieved: 0,
                Underperforming: 0,
                Pending: 3,
                NotApplicable: 0,
                Mixed: 0,
                AvgReturnPercent: null),
        };

        var record = new DecisionTrackRecord(rows);

        record.BySource[0].AvgReturnPercent.ShouldBeNull();
    }

    [Fact]
    public void Empty_BySource_list_is_valid()
    {
        var record = new DecisionTrackRecord(Array.Empty<DecisionTrackRecordRow>());
        record.BySource.ShouldBeEmpty();
    }
}
