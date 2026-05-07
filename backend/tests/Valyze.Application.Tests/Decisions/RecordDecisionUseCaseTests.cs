using Shouldly;
using Valyze.Application.Decisions;
using Valyze.Domain.Application.Decisions;
using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Enum;
using Valyze.Domain.Exceptions;
using Valyze.Domain.Money;
using Valyze.Domain.QueryService;
using Valyze.Domain.Repository;
using Xunit;

namespace Valyze.Application.Tests.Decisions;

public sealed class RecordDecisionUseCaseTests
{
    // ─── Hand-rolled fakes ────────────────────────────────────────────────────

    private sealed class FakeDecisionRepository : IInvestmentDecisionRepository
    {
        public InvestmentDecisionEntity? LastCreated { get; private set; }
        private readonly Guid _returnId;

        public FakeDecisionRepository(Guid? returnId = null)
            => _returnId = returnId ?? Guid.NewGuid();

        public Task<Guid> CreateAsync(InvestmentDecisionEntity decision, CancellationToken cancellationToken = default)
        {
            LastCreated = decision;
            return Task.FromResult(_returnId);
        }

        public Task UpdateLinkedTradeAsync(Guid decisionId, Guid accountId, Guid? tradeId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<InvestmentDecisionEntity?> GetByIdForAccountAsync(Guid decisionId, Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult<InvestmentDecisionEntity?>(null);
    }

    private sealed class FakePriceQuoteQueryService : IPriceQuoteQueryService
    {
        private readonly IReadOnlyList<PriceQuoteEntity> _quotes;

        public FakePriceQuoteQueryService(params PriceQuoteEntity[] quotes)
            => _quotes = quotes;

        public Task<IReadOnlyList<PriceQuoteEntity>> GetFreshAsync(
            IReadOnlyCollection<string> symbols,
            Currency currency,
            DateTimeOffset freshSince,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_quotes);

        public Task<Money?> GetLatestForSymbolAsync(
            string symbol,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Money?>(null);
    }

    private static RecordDecisionCommand BuildCommand(
        Guid? accountId = null,
        DecisionSource source = DecisionSource.UserOwnAnalysis,
        DecisionAction action = DecisionAction.Buy,
        string? isin = "IE00B4L5Y983",
        int? horizonDays = null,
        string rationale = "Test rationale",
        QuantityUnits units = QuantityUnits.Shares,
        decimal? quantityAmount = 10m)
        => new(
            AccountId: accountId ?? Guid.NewGuid(),
            Source: source,
            Action: action,
            Isin: isin,
            Ticker: null,
            QuantityAmount: quantityAmount,
            QuantityCurrency: null,
            QuantityUnits: units,
            Rationale: rationale,
            EvaluationHorizonDays: horizonDays,
            SourceOtherNote: null);

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Records_decision_with_AI_recommendation_source_and_price_snapshot()
    {
        var accountId = Guid.NewGuid();
        var expectedId = Guid.NewGuid();
        var repo = new FakeDecisionRepository(expectedId);
        var priceService = new FakePriceQuoteQueryService(new PriceQuoteEntity
        {
            Symbol = "IE00B4L5Y983",
            Currency = Currency.Eur,
            Amount = 100.00m,
            Source = "yahoo",
            FetchedAt = DateTimeOffset.UtcNow,
        });

        var useCase = new RecordDecisionUseCase(repo, priceService);
        var command = BuildCommand(
            accountId: accountId,
            source: DecisionSource.AiRecommendation,
            action: DecisionAction.Buy,
            isin: "IE00B4L5Y983");

        var result = await useCase.ExecuteAsync(command);

        result.Id.ShouldBe(expectedId);
        result.Warning.ShouldBeNull();
        repo.LastCreated.ShouldNotBeNull();
        repo.LastCreated!.AccountId.ShouldBe(accountId);
        repo.LastCreated.Source.ShouldBe(DecisionSource.AiRecommendation);
        repo.LastCreated.PriceAtDecision.ShouldNotBeNull();
        repo.LastCreated.PriceAtDecision!.Value.Amount.ShouldBe(100.00m);
        repo.LastCreated.EvaluationHorizonDays.ShouldBe(180); // BUY default
    }

    [Fact]
    public async Task Records_decision_with_null_price_snapshot_when_feed_unavailable()
    {
        var repo = new FakeDecisionRepository();
        var emptyPriceService = new FakePriceQuoteQueryService(); // no quotes

        var useCase = new RecordDecisionUseCase(repo, emptyPriceService);
        var command = BuildCommand(isin: "XX1234567890", action: DecisionAction.Buy);

        var result = await useCase.ExecuteAsync(command);

        result.Warning.ShouldNotBeNullOrWhiteSpace();
        repo.LastCreated.ShouldNotBeNull();
        repo.LastCreated!.PriceAtDecision.ShouldBeNull();
    }

    [Theory]
    [InlineData(DecisionAction.Buy, 180)]
    [InlineData(DecisionAction.Sell, 30)]
    [InlineData(DecisionAction.Hold, 90)]
    [InlineData(DecisionAction.Rebalance, 90)]
    public async Task Applies_default_horizon_when_not_supplied(DecisionAction action, int expectedHorizon)
    {
        var repo = new FakeDecisionRepository();
        var priceService = new FakePriceQuoteQueryService();

        var useCase = new RecordDecisionUseCase(repo, priceService);
        var command = BuildCommand(action: action, horizonDays: null, isin: null);

        await useCase.ExecuteAsync(command);

        repo.LastCreated!.EvaluationHorizonDays.ShouldBe(expectedHorizon);
    }

    [Fact]
    public async Task Respects_caller_supplied_horizon_when_provided()
    {
        var repo = new FakeDecisionRepository();
        var priceService = new FakePriceQuoteQueryService();

        var useCase = new RecordDecisionUseCase(repo, priceService);
        var command = BuildCommand(action: DecisionAction.Sell, horizonDays: 60);

        await useCase.ExecuteAsync(command);

        repo.LastCreated!.EvaluationHorizonDays.ShouldBe(60);
    }

    [Fact]
    public async Task Throws_BusinessException_when_AccountId_is_empty()
    {
        var repo = new FakeDecisionRepository();
        var priceService = new FakePriceQuoteQueryService();

        var useCase = new RecordDecisionUseCase(repo, priceService);
        var command = BuildCommand(accountId: Guid.Empty);

        await Should.ThrowAsync<BusinessException>(() => useCase.ExecuteAsync(command));
    }

    [Fact]
    public async Task Throws_BusinessException_when_Rationale_is_empty()
    {
        var repo = new FakeDecisionRepository();
        var priceService = new FakePriceQuoteQueryService();

        var useCase = new RecordDecisionUseCase(repo, priceService);
        var command = BuildCommand(rationale: "");

        await Should.ThrowAsync<BusinessException>(() => useCase.ExecuteAsync(command));
    }
}
