using Valyze.Domain.Entities.News;

namespace Valyze.Domain.Application.News;

public interface IGetNewsForSymbolUseCase
{
    Task<IReadOnlyList<NewsArticleEntity>> ExecuteAsync(
        string instrument,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken = default);
}

public interface IGetLatestNewsUseCase
{
    Task<IReadOnlyList<NewsArticleEntity>> ExecuteAsync(
        Guid accountId,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Forces an immediate poll of every enabled source. Useful from the chat
/// ("hace un refresh ya") and from a manual button in the UI later.
/// </summary>
public interface IRefreshNewsUseCase
{
    Task<RefreshNewsResult> ExecuteAsync(CancellationToken cancellationToken = default);
}

public sealed class RefreshNewsResult
{
    public int SourcesPolled { get; set; }
    public int ArticlesAdded { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = [];
}
