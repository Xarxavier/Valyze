using Microsoft.Extensions.Options;
using Shouldly;
using Valyze.Application.Decisions;
using Valyze.Domain.Application.Decisions;
using Valyze.Domain.Decisions;
using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Enum;
using Valyze.Domain.Exceptions;
using Valyze.Domain.Money;
using Valyze.Domain.QueryService;
using Valyze.Domain.Repository;
using Xunit;

namespace Valyze.Application.Tests.Decisions;

public sealed class EvaluateDecisionUseCaseTests
{
    // ─── Hand-rolled fakes ────────────────────────────────────────────────────

    private sealed class FakeDecisionRepository : IInvestmentDecisionRepository
    {
        private readonly InvestmentDecisionEntity? _entity;

        public FakeDecisionRepository(InvestmentDecisionEntity? entity = null)
            => _entity = entity;

        public Task<Guid> CreateAsync(InvestmentDecisionEntity decision, CancellationToken cancellationToken = default)
            => Task.FromResult(decision.Id);

        public Task UpdateLinkedTradeAsync(Guid decisionId, Guid accountId, Guid? tradeId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<InvestmentDecisionEntity?> GetByIdForAccountAsync(Guid decisionId, Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(_entity);
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

    private static IOptions<DecisionEvaluationOptions> DefaultOptions(decimal threshold = 0.05m)
        => Options.Create(new DecisionEvaluationOptions { AchievementThreshold = threshold });

    private static InvestmentDecisionEntity BuildDecision(
        Guid? accountId = null,
        DecisionAction action = DecisionAction.Buy,
        string? isin = "IE00B4L5Y983",
        decimal? priceAmount = 100m,
        int horizonDays = 180,
        int daysAgo = 200)
    {
        var aid = accountId ?? Guid.NewGuid();
        return new InvestmentDecisionEntity
        {
            Id = Guid.NewGuid(),
            AccountId = aid,
            Source = DecisionSource.UserOwnAnalysis,
            Action = action,
            Isin = isin,
            QuantityUnits = QuantityUnits.Shares,
            PriceAtDecision = priceAmount.HasValue
                ? new Money(priceAmount.Value, Currency.Eur)
                : null,
            Rationale = "Test",
            EvaluationHorizonDays = horizonDays,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
        };
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_PendingHorizon_when_days_elapsed_less_than_horizon()
    {
        var accountId = Guid.NewGuid();
        var decision = BuildDecision(accountId: accountId, daysAgo: 30, horizonDays: 180);
        var repo = new FakeDecisionRepository(decision);
        var priceService = new FakePriceQuoteQueryService(new PriceQuoteEntity
        {
            Symbol = "IE00B4L5Y983",
            Currency = Currency.Eur,
            Amount = 105m,
            Source = "yahoo",
            FetchedAt = DateTimeOffset.UtcNow,
        });

        var useCase = new EvaluateDecisionUseCase(repo, priceService, DefaultOptions());
        var eval = await useCase.ExecuteAsync(decision.Id, accountId);

        eval.Status.ShouldBe(DecisionStatus.PendingHorizon);
        eval.ReturnPercent.ShouldBeNull();
        eval.DaysElapsed.ShouldBeInRange(29, 31);
        eval.Horizon.ShouldBe(180);
    }

    [Fact]
    public async Task Returns_Achieved_when_BUY_price_up_beyond_threshold()
    {
        var accountId = Guid.NewGuid();
        var decision = BuildDecision(accountId: accountId, action: DecisionAction.Buy, priceAmount: 100m, daysAgo: 200, horizonDays: 180);
        var repo = new FakeDecisionRepository(decision);
        var priceService = new FakePriceQuoteQueryService(new PriceQuoteEntity
        {
            Symbol = "IE00B4L5Y983",
            Currency = Currency.Eur,
            Amount = 110m,
            Source = "yahoo",
            FetchedAt = DateTimeOffset.UtcNow,
        });

        var useCase = new EvaluateDecisionUseCase(repo, priceService, DefaultOptions());
        var eval = await useCase.ExecuteAsync(decision.Id, accountId);

        eval.Status.ShouldBe(DecisionStatus.Achieved);
        eval.ReturnPercent.ShouldBe(10m);
    }

    [Fact]
    public async Task Returns_Underperforming_when_BUY_price_down_beyond_threshold()
    {
        var accountId = Guid.NewGuid();
        var decision = BuildDecision(accountId: accountId, action: DecisionAction.Buy, priceAmount: 100m, daysAgo: 200, horizonDays: 180);
        var repo = new FakeDecisionRepository(decision);
        var priceService = new FakePriceQuoteQueryService(new PriceQuoteEntity
        {
            Symbol = "IE00B4L5Y983",
            Currency = Currency.Eur,
            Amount = 88m,
            Source = "yahoo",
            FetchedAt = DateTimeOffset.UtcNow,
        });

        var useCase = new EvaluateDecisionUseCase(repo, priceService, DefaultOptions());
        var eval = await useCase.ExecuteAsync(decision.Id, accountId);

        eval.Status.ShouldBe(DecisionStatus.Underperforming);
        eval.ReturnPercent.ShouldBe(-12m);
    }

    [Fact]
    public async Task Returns_Achieved_when_BUY_price_up_within_threshold_boundary()
    {
        // +3% is positive — any positive return is ACHIEVED (threshold only gates UNDERPERFORMING side)
        var accountId = Guid.NewGuid();
        var decision = BuildDecision(accountId: accountId, action: DecisionAction.Buy, priceAmount: 100m, daysAgo: 200, horizonDays: 180);
        var repo = new FakeDecisionRepository(decision);
        var priceService = new FakePriceQuoteQueryService(new PriceQuoteEntity
        {
            Symbol = "IE00B4L5Y983",
            Currency = Currency.Eur,
            Amount = 103m,
            Source = "yahoo",
            FetchedAt = DateTimeOffset.UtcNow,
        });

        var useCase = new EvaluateDecisionUseCase(repo, priceService, DefaultOptions());
        var eval = await useCase.ExecuteAsync(decision.Id, accountId);

        eval.Status.ShouldBe(DecisionStatus.Achieved);
        eval.ReturnPercent.ShouldBe(3m);
    }

    [Fact]
    public async Task Returns_NotApplicable_with_message_when_price_snapshot_is_null()
    {
        var accountId = Guid.NewGuid();
        var decision = BuildDecision(accountId: accountId, priceAmount: null, daysAgo: 200, horizonDays: 180);
        var repo = new FakeDecisionRepository(decision);
        var priceService = new FakePriceQuoteQueryService();

        var useCase = new EvaluateDecisionUseCase(repo, priceService, DefaultOptions());
        var eval = await useCase.ExecuteAsync(decision.Id, accountId);

        eval.Status.ShouldBe(DecisionStatus.NotApplicable);
        eval.ReturnPercent.ShouldBeNull();
        eval.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Returns_NotApplicable_for_HOLD_without_instrument()
    {
        var accountId = Guid.NewGuid();
        var decision = BuildDecision(accountId: accountId, action: DecisionAction.Hold, isin: null, priceAmount: null, daysAgo: 200, horizonDays: 90);
        var repo = new FakeDecisionRepository(decision);
        var priceService = new FakePriceQuoteQueryService();

        var useCase = new EvaluateDecisionUseCase(repo, priceService, DefaultOptions());
        var eval = await useCase.ExecuteAsync(decision.Id, accountId);

        eval.Status.ShouldBe(DecisionStatus.NotApplicable);
        eval.ReturnPercent.ShouldBeNull();
    }

    [Fact]
    public async Task Returns_Mixed_for_REBALANCE_without_instrument()
    {
        var accountId = Guid.NewGuid();
        var decision = BuildDecision(accountId: accountId, action: DecisionAction.Rebalance, isin: null, priceAmount: null, daysAgo: 100, horizonDays: 90);
        var repo = new FakeDecisionRepository(decision);
        var priceService = new FakePriceQuoteQueryService();

        var useCase = new EvaluateDecisionUseCase(repo, priceService, DefaultOptions());
        var eval = await useCase.ExecuteAsync(decision.Id, accountId);

        eval.Status.ShouldBe(DecisionStatus.Mixed);
        eval.ReturnPercent.ShouldBeNull();
    }

    [Fact]
    public async Task Throws_BusinessException_when_decision_not_found_for_account()
    {
        var repo = new FakeDecisionRepository(entity: null);
        var priceService = new FakePriceQuoteQueryService();

        var useCase = new EvaluateDecisionUseCase(repo, priceService, DefaultOptions());

        await Should.ThrowAsync<BusinessException>(
            () => useCase.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task Returns_Achieved_for_SELL_when_price_dropped_beyond_threshold()
    {
        // SELL: favorable = price dropped (returnPct <= -threshold)
        var accountId = Guid.NewGuid();
        var decision = BuildDecision(accountId: accountId, action: DecisionAction.Sell, priceAmount: 100m, daysAgo: 50, horizonDays: 30);
        var repo = new FakeDecisionRepository(decision);
        var priceService = new FakePriceQuoteQueryService(new PriceQuoteEntity
        {
            Symbol = "IE00B4L5Y983",
            Currency = Currency.Eur,
            Amount = 90m, // -10%, good for SELL
            Source = "yahoo",
            FetchedAt = DateTimeOffset.UtcNow,
        });

        var useCase = new EvaluateDecisionUseCase(repo, priceService, DefaultOptions());
        var eval = await useCase.ExecuteAsync(decision.Id, accountId);

        eval.Status.ShouldBe(DecisionStatus.Achieved);
        eval.ReturnPercent.ShouldBe(-10m);
    }
}
