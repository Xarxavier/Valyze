using Valyze.Domain.Entities.News;

namespace Valyze.Domain.Repository;

public interface INewsSourceRepository
{
    Task<NewsSourceEntity> CreateAsync(NewsSourceEntity source, CancellationToken cancellationToken = default);

    Task<NewsSourceEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates polling state (LastPolledAt + LastError). Used by the collector
    /// to persist health regardless of whether the fetch yielded new articles.
    /// </summary>
    Task UpdatePollingStateAsync(
        Guid id,
        DateTimeOffset polledAt,
        string? lastError,
        CancellationToken cancellationToken = default);

    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default);
}
