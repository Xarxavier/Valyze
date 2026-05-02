using Microsoft.EntityFrameworkCore;
using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Repository;
using Valyze.Infraestructure.EntityFramework;
using Valyze.Infraestructure.EntityFramework.Mapper;

namespace Valyze.Infraestructure.Repository.Portfolio;

public class TradeRepository : ITradeRepository
{
    private readonly ValyzeDbContext _context;

    public TradeRepository(ValyzeDbContext context)
    {
        _context = context;
    }

    public async Task<TradeEntity> CreateAsync(TradeEntity trade, CancellationToken cancellationToken = default)
    {
        var ef = TradeMapper.ToEf(trade);
        _context.Trades.Add(ef);
        await _context.SaveChangesAsync(cancellationToken);
        return TradeMapper.ToDomain(ef);
    }

    public async Task<IReadOnlyList<TradeEntity>> CreateManyAsync(
        IEnumerable<TradeEntity> trades,
        CancellationToken cancellationToken = default)
    {
        var efs = trades.Select(TradeMapper.ToEf).ToList();
        _context.Trades.AddRange(efs);
        await _context.SaveChangesAsync(cancellationToken);
        return efs.Select(TradeMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlySet<string>> FindExistingReferencesAsync(
        Guid accountId,
        string brokerKey,
        IReadOnlyCollection<string> references,
        CancellationToken cancellationToken = default)
    {
        if (references.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

        var found = await _context.Trades
            .Where(t => t.AccountId == accountId
                        && t.BrokerKey == brokerKey
                        && t.BrokerReference != null
                        && references.Contains(t.BrokerReference!))
            .Select(t => t.BrokerReference!)
            .ToListAsync(cancellationToken);

        return new HashSet<string>(found, StringComparer.Ordinal);
    }
}
