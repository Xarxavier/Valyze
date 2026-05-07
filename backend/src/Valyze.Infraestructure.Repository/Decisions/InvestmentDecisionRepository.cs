using Microsoft.EntityFrameworkCore;
using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Exceptions;
using Valyze.Domain.Repository;
using Valyze.Infraestructure.EntityFramework;
using Valyze.Infraestructure.EntityFramework.Mapper;

namespace Valyze.Infraestructure.Repository.Decisions;

public class InvestmentDecisionRepository : IInvestmentDecisionRepository
{
    private readonly ValyzeDbContext _context;

    public InvestmentDecisionRepository(ValyzeDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<Guid> CreateAsync(
        InvestmentDecisionEntity decision,
        CancellationToken cancellationToken = default)
    {
        var ef = InvestmentDecisionMapper.ToEf(decision);
        _context.InvestmentDecisions.Add(ef);
        await _context.SaveChangesAsync(cancellationToken);
        return ef.Id;
    }

    /// <inheritdoc/>
    public async Task UpdateLinkedTradeAsync(
        Guid decisionId,
        Guid accountId,
        Guid? tradeId,
        CancellationToken cancellationToken = default)
    {
        var ef = await _context.InvestmentDecisions
            .Where(d => d.Id == decisionId && d.AccountId == accountId)
            .FirstOrDefaultAsync(cancellationToken);

        if (ef is null)
            throw new BusinessException(
                "msnDecisionNotFound",
                $"Decision {decisionId} not found for account {accountId}.");

        // If linking to a specific trade, verify that trade belongs to the same account.
        if (tradeId.HasValue)
        {
            var tradeExists = await _context.Trades
                .AnyAsync(t => t.Id == tradeId.Value && t.AccountId == accountId, cancellationToken);

            if (!tradeExists)
                throw new BusinessException(
                    "msnTradeNotFound",
                    $"Trade {tradeId.Value} not found for account {accountId}.");
        }

        ef.LinkedTradeId = tradeId;
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<InvestmentDecisionEntity?> GetByIdForAccountAsync(
        Guid decisionId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var ef = await _context.InvestmentDecisions
            .Where(d => d.Id == decisionId && d.AccountId == accountId)
            .FirstOrDefaultAsync(cancellationToken);

        return ef is null ? null : InvestmentDecisionMapper.ToDomain(ef);
    }
}
