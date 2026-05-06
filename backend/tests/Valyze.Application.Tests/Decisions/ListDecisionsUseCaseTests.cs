using Shouldly;
using Valyze.Application.Decisions;
using Valyze.Domain.Application.Decisions;
using Valyze.Domain.Decisions;
using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Enum;
using Valyze.Domain.Exceptions;
using Valyze.Domain.QueryService;
using Xunit;

namespace Valyze.Application.Tests.Decisions;

public sealed class ListDecisionsUseCaseTests
{
    // ─── Hand-rolled fakes ────────────────────────────────────────────────────

    private sealed class FakeDecisionQueryService : IInvestmentDecisionQueryService
    {
        private readonly IReadOnlyList<InvestmentDecisionEntity> _decisions;

        public ListDecisionsQuery? LastQuery { get; private set; }

        public FakeDecisionQueryService(params InvestmentDecisionEntity[] decisions)
            => _decisions = decisions;

        public Task<IReadOnlyList<InvestmentDecisionEntity>> ListByAccountAsync(
            Guid accountId,
            ListDecisionsQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(_decisions);
        }

        public Task<IReadOnlyList<DecisionTrackRecordRow>> GetTrackRecordAsync(
            Guid accountId,
            DecisionSource? sourceFilter,
            decimal achievementThreshold,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DecisionTrackRecordRow>>([]);
    }

    private static InvestmentDecisionEntity BuildDecision(Guid accountId, DecisionSource source = DecisionSource.UserOwnAnalysis)
        => new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Source = source,
            Action = DecisionAction.Buy,
            QuantityUnits = QuantityUnits.Shares,
            Rationale = "Test",
            EvaluationHorizonDays = 180,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_decisions_from_query_service()
    {
        var accountId = Guid.NewGuid();
        var d1 = BuildDecision(accountId);
        var d2 = BuildDecision(accountId);
        var queryService = new FakeDecisionQueryService(d1, d2);

        var useCase = new ListDecisionsUseCase(queryService);
        var query = new ListDecisionsQuery(accountId, null, null, null, null, null);

        var result = await useCase.ExecuteAsync(query);

        result.Count.ShouldBe(2);
        result.ShouldContain(d1);
        result.ShouldContain(d2);
    }

    [Fact]
    public async Task Passes_filters_to_query_service()
    {
        var accountId = Guid.NewGuid();
        var queryService = new FakeDecisionQueryService();

        var useCase = new ListDecisionsUseCase(queryService);
        var query = new ListDecisionsQuery(
            accountId,
            Limit: 5,
            Since: DateTimeOffset.UtcNow.AddDays(-30),
            Source: DecisionSource.AiRecommendation,
            Action: DecisionAction.Buy,
            Isin: "IE00B4L5Y983");

        await useCase.ExecuteAsync(query);

        queryService.LastQuery.ShouldNotBeNull();
        queryService.LastQuery!.Limit.ShouldBe(5);
        queryService.LastQuery.Source.ShouldBe(DecisionSource.AiRecommendation);
        queryService.LastQuery.Action.ShouldBe(DecisionAction.Buy);
        queryService.LastQuery.Isin.ShouldBe("IE00B4L5Y983");
    }

    [Fact]
    public async Task Returns_empty_list_when_no_decisions()
    {
        var accountId = Guid.NewGuid();
        var queryService = new FakeDecisionQueryService();

        var useCase = new ListDecisionsUseCase(queryService);
        var query = new ListDecisionsQuery(accountId, null, null, null, null, null);

        var result = await useCase.ExecuteAsync(query);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Throws_BusinessException_when_AccountId_is_empty()
    {
        var queryService = new FakeDecisionQueryService();
        var useCase = new ListDecisionsUseCase(queryService);
        var query = new ListDecisionsQuery(Guid.Empty, null, null, null, null, null);

        await Should.ThrowAsync<BusinessException>(() => useCase.ExecuteAsync(query));
    }
}
