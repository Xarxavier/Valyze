using Microsoft.EntityFrameworkCore;
using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Repository;
using Valyze.Infraestructure.EntityFramework;
using Valyze.Infraestructure.EntityFramework.Mapper;

namespace Valyze.Infraestructure.Repository.MarketData;

public class PriceQuoteRepository : IPriceQuoteRepository
{
    private readonly ValyzeDbContext _context;

    public PriceQuoteRepository(ValyzeDbContext context)
    {
        _context = context;
    }

    public async Task UpsertManyAsync(
        IEnumerable<PriceQuoteEntity> quotes,
        CancellationToken cancellationToken = default)
    {
        var batch = quotes.ToList();
        if (batch.Count == 0) return;

        // Pre-load existing rows for the (symbol, currency) pairs we're upserting
        // so we can mutate them in-place. EF Core lacks a generic upsert; per-row
        // FirstOrDefault would be N round-trips. One IN query is good enough for
        // the cache size we expect (tens of symbols max).
        var symbols = batch.Select(q => q.Symbol).Distinct().ToList();
        var currencies = batch.Select(q => q.Currency.Code).Distinct().ToList();

        var existing = await _context.PriceQuotes
            .Where(q => symbols.Contains(q.Symbol) && currencies.Contains(q.Currency))
            .ToListAsync(cancellationToken);

        var index = existing.ToDictionary(
            q => (q.Symbol, q.Currency),
            StringTupleComparer.Instance);

        foreach (var quote in batch)
        {
            var key = (quote.Symbol, quote.Currency.Code);
            if (index.TryGetValue(key, out var row))
            {
                row.Amount = quote.Amount;
                row.Source = quote.Source;
                row.FetchedAt = quote.FetchedAt;
            }
            else
            {
                _context.PriceQuotes.Add(PriceQuoteMapper.ToEf(quote));
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string, string) x, (string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string) obj) =>
            HashCode.Combine(
                obj.Item1.ToUpperInvariant(),
                obj.Item2.ToUpperInvariant());
    }
}
