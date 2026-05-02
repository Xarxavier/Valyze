using Microsoft.EntityFrameworkCore;
using Valyze.Domain.Entities.News;
using Valyze.Domain.Repository;
using Valyze.Infraestructure.EntityFramework;
using Valyze.Infraestructure.EntityFramework.Mapper;
using EfEntities = Valyze.Infraestructure.EntityFramework.Entities;

namespace Valyze.Infraestructure.Repository.News;

public class NewsArticleRepository : INewsArticleRepository
{
    private readonly ValyzeDbContext _context;

    public NewsArticleRepository(ValyzeDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<NewsArticleEntity>> UpsertManyAsync(
        IEnumerable<NewsArticleEntity> articles,
        CancellationToken cancellationToken = default)
    {
        var batch = articles.ToList();
        if (batch.Count == 0) return [];

        // Url is the dedup key. Pull existing URLs in one shot to skip them
        // — pure inserts let us use AddRange + a single SaveChanges.
        var urls = batch.Select(a => a.Url).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existing = await _context.NewsArticles
            .AsNoTracking()
            .Where(a => urls.Contains(a.Url))
            .Select(a => a.Url)
            .ToListAsync(cancellationToken);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        // Dedup WITHIN the batch too — the same Google News article often
        // surfaces under several per-symbol expansions (e.g. "Tesla" and
        // "TSLA"). Keep the first occurrence; any per-fetch instrument hint
        // for the dropped duplicates is preserved separately by the caller.
        var newOnes = batch
            .Where(a => !existingSet.Contains(a.Url))
            .GroupBy(a => a.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (newOnes.Count == 0) return [];

        var efRows = new List<EfEntities.NewsArticle>(newOnes.Count);
        foreach (var article in newOnes)
        {
            if (article.Id == Guid.Empty) article.Id = Guid.NewGuid();
            efRows.Add(NewsArticleMapper.ToEf(article));
        }

        _context.NewsArticles.AddRange(efRows);
        await _context.SaveChangesAsync(cancellationToken);

        // Return the domain shapes with their assigned ids — the caller uses
        // those to attach instrument tags.
        return efRows.Select(r => NewsArticleMapper.ToDomain(r)).ToList();
    }

    public async Task TagInstrumentsAsync(
        Guid articleId,
        IEnumerable<NewsArticleInstrumentEntity> tags,
        CancellationToken cancellationToken = default)
    {
        var newTags = tags.ToList();

        // Replace strategy: drop existing tags for this article, write fresh ones.
        var existing = await _context.NewsArticleInstruments
            .Where(t => t.ArticleId == articleId)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            _context.NewsArticleInstruments.RemoveRange(existing);
        }

        foreach (var tag in newTags)
        {
            _context.NewsArticleInstruments.Add(new EfEntities.NewsArticleInstrument
            {
                ArticleId = articleId,
                Instrument = tag.Instrument,
                Confidence = tag.Confidence,
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
