using Microsoft.Extensions.Options;
using Shouldly;
using Valyze.Application.Decisions;
using Valyze.Domain.Application.Decisions;
using Valyze.Domain.Decisions;
using Valyze.Domain.Enum;
using Valyze.Domain.Exceptions;
using Valyze.Domain.QueryService;
using Xunit;

namespace Valyze.Application.Tests.Decisions;

public sealed class GetDecisionTrackRecordUseCaseTests
{
    // ─── Hand-rolled fakes ────────────────────────────────────────────────────

    private sealed class FakeDecisionQueryService : IInvestmentDecisionQueryService
    {
        private readonly IReadOnlyList<DecisionTrackRecordRow> _rows;
        public DecisionSource? LastSourceFilter { get; private set; }
        public decimal? LastThreshold { get; private set; }

        public FakeDecisionQueryService(params DecisionTrackRecordRow[] rows)
            => _rows = rows;

        public Task<IReadOnlyList<Domain.Entities.Decisions.InvestmentDecisionEntity>> ListByAccountAsync(
            Guid accountId,
            ListDecisionsQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Domain.Entities.Decisions.InvestmentDecisionEntity>>([]);

        public Task<IReadOnlyList<DecisionTrackRecordRow>> GetTrackRecordAsync(
            Guid accountId,
            DecisionSource? sourceFilter,
            decimal achievementThreshold,
            CancellationToken cancellationToken = default)
        {
            LastSourceFilter = sourceFilter;
            LastThreshold = achievementThreshold;
            return Task.FromResult(_rows);
        }
    }

    private static IOptions<DecisionEvaluationOptions> DefaultOptions(decimal threshold = 0.05m)
        => Options.Create(new DecisionEvaluationOptions { AchievementThreshold = threshold });

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_track_record_aggregating_rows_from_query_service()
    {
        var accountId = Guid.NewGuid();
        var row = new DecisionTrackRecordRow(
            Source: DecisionSource.AiRecommendation,
            Total: 4,
            Achieved: 3,
            Underperforming: 1,
            Pending: 0,
            NotApplicable: 0,
            Mixed: 0,
            AvgReturnPercent: 8.5m);

        var queryService = new FakeDecisionQueryService(row);
        var useCase = new GetDecisionTrackRecordUseCase(queryService, DefaultOptions());

        var result = await useCase.ExecuteAsync(accountId, sourceFilter: null);

        result.BySource.Count.ShouldBe(1);
        result.BySource[0].Source.ShouldBe(DecisionSource.AiRecommendation);
        result.BySource[0].Total.ShouldBe(4);
        result.BySource[0].Achieved.ShouldBe(3);
    }

    [Fact]
    public async Task Returns_empty_track_record_for_account_with_no_decisions()
    {
        var accountId = Guid.NewGuid();
        var queryService = new FakeDecisionQueryService(); // no rows

        var useCase = new GetDecisionTrackRecordUseCase(queryService, DefaultOptions());
        var result = await useCase.ExecuteAsync(accountId, sourceFilter: null);

        result.BySource.ShouldBeEmpty();
    }

    [Fact]
    public async Task Passes_source_filter_and_threshold_to_query_service()
    {
        var accountId = Guid.NewGuid();
        var queryService = new FakeDecisionQueryService();

        var useCase = new GetDecisionTrackRecordUseCase(queryService, DefaultOptions(threshold: 0.07m));
        await useCase.ExecuteAsync(accountId, sourceFilter: DecisionSource.ExternalNews);

        queryService.LastSourceFilter.ShouldBe(DecisionSource.ExternalNews);
        queryService.LastThreshold.ShouldBe(0.07m);
    }

    [Fact]
    public async Task Throws_BusinessException_when_AccountId_is_empty()
    {
        var queryService = new FakeDecisionQueryService();
        var useCase = new GetDecisionTrackRecordUseCase(queryService, DefaultOptions());

        await Should.ThrowAsync<BusinessException>(
            () => useCase.ExecuteAsync(Guid.Empty, sourceFilter: null));
    }
}
