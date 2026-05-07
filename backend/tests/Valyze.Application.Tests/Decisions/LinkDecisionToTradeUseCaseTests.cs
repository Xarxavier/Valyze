using Shouldly;
using Valyze.Application.Decisions;
using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Enum;
using Valyze.Domain.Exceptions;
using Valyze.Domain.Repository;
using Xunit;

namespace Valyze.Application.Tests.Decisions;

public sealed class LinkDecisionToTradeUseCaseTests
{
    // ─── Hand-rolled fakes ────────────────────────────────────────────────────

    private sealed class FakeDecisionRepository : IInvestmentDecisionRepository
    {
        private readonly InvestmentDecisionEntity? _entity;
        public Guid? LastLinkedTradeId { get; private set; }
        public bool UpdateCalled { get; private set; }
        private readonly bool _updateThrows;

        public FakeDecisionRepository(
            InvestmentDecisionEntity? entity = null,
            bool updateThrows = false)
        {
            _entity = entity;
            _updateThrows = updateThrows;
        }

        public Task<Guid> CreateAsync(InvestmentDecisionEntity decision, CancellationToken cancellationToken = default)
            => Task.FromResult(decision.Id);

        public Task UpdateLinkedTradeAsync(Guid decisionId, Guid accountId, Guid? tradeId, CancellationToken cancellationToken = default)
        {
            if (_updateThrows)
                throw new BusinessException("msnTradeNotFoundForAccount");

            UpdateCalled = true;
            LastLinkedTradeId = tradeId;
            return Task.CompletedTask;
        }

        public Task<InvestmentDecisionEntity?> GetByIdForAccountAsync(Guid decisionId, Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(_entity);
    }

    private static InvestmentDecisionEntity BuildDecision(Guid accountId)
        => new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Source = DecisionSource.UserOwnAnalysis,
            Action = DecisionAction.Buy,
            QuantityUnits = QuantityUnits.Shares,
            Rationale = "Test",
            EvaluationHorizonDays = 180,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Links_trade_to_decision_when_both_belong_to_same_account()
    {
        var accountId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var decision = BuildDecision(accountId);
        var repo = new FakeDecisionRepository(decision);

        var useCase = new LinkDecisionToTradeUseCase(repo);
        await useCase.ExecuteAsync(decision.Id, accountId, tradeId);

        repo.UpdateCalled.ShouldBeTrue();
        repo.LastLinkedTradeId.ShouldBe(tradeId);
    }

    [Fact]
    public async Task Clears_linked_trade_when_tradeId_is_null()
    {
        var accountId = Guid.NewGuid();
        var decision = BuildDecision(accountId);
        var repo = new FakeDecisionRepository(decision);

        var useCase = new LinkDecisionToTradeUseCase(repo);
        await useCase.ExecuteAsync(decision.Id, accountId, tradeId: null);

        repo.UpdateCalled.ShouldBeTrue();
        repo.LastLinkedTradeId.ShouldBeNull();
    }

    [Fact]
    public async Task Throws_BusinessException_when_decision_not_found_for_account()
    {
        // GetByIdForAccountAsync returns null → decision doesn't belong to this account
        var repo = new FakeDecisionRepository(entity: null);
        var useCase = new LinkDecisionToTradeUseCase(repo);

        await Should.ThrowAsync<BusinessException>(
            () => useCase.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task Propagates_BusinessException_from_repo_when_trade_from_another_account()
    {
        // Repository throws when trade doesn't belong to the same account (cross-account guard at repo layer)
        var accountId = Guid.NewGuid();
        var decision = BuildDecision(accountId);
        var repo = new FakeDecisionRepository(decision, updateThrows: true);
        var useCase = new LinkDecisionToTradeUseCase(repo);

        await Should.ThrowAsync<BusinessException>(
            () => useCase.ExecuteAsync(decision.Id, accountId, Guid.NewGuid()));
    }
}
