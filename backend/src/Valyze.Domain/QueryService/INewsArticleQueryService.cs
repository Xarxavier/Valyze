using Valyze.Domain.Entities.News;

namespace Valyze.Domain.QueryService;

public interface INewsArticleQueryService
{
    /// <summary>
    /// Articles tagged against the given instrument (ISIN or ticker, case-insensitive),
    /// newest first. <paramref name="since"/> is inclusive on PublishedAt.
    /// </summary>
    Task<IReadOnlyList<NewsArticleEntity>> GetForSymbolAsync(
        string instrument,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Latest articles across all sources, restricted to instruments held in
    /// the given account. <paramref name="since"/> is inclusive on PublishedAt.
    /// </summary>
    Task<IReadOnlyList<NewsArticleEntity>> GetLatestForAccountAsync(
        Guid accountId,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Distinct (instrument, friendly name) pairs for an account, used by the collector.</summary>
    Task<IReadOnlyList<(string Instrument, string? Name)>> GetTrackedInstrumentsAsync(
        CancellationToken cancellationToken = default);
}
