using Valyze.Domain.Entities.News;

namespace Valyze.Domain.Repository;

public interface INewsArticleRepository
{
    /// <summary>
    /// Inserts the articles whose <c>Url</c> isn't already stored. Returns the
    /// articles actually persisted (with their assigned <c>Id</c>) so the
    /// caller can run instrument tagging only on the new rows.
    /// </summary>
    Task<IReadOnlyList<NewsArticleEntity>> UpsertManyAsync(
        IEnumerable<NewsArticleEntity> articles,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the instrument tags for an article. Idempotent — used after
    /// the collector tags newly-fetched rows.
    /// </summary>
    Task TagInstrumentsAsync(
        Guid articleId,
        IEnumerable<NewsArticleInstrumentEntity> tags,
        CancellationToken cancellationToken = default);
}
