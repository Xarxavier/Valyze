namespace Valyze.Domain.Application.News;

/// <summary>
/// Pulls a single news feed and returns parsed items. Implementations are
/// kind-specific (RSS in v1; future: JSON Feed, Reddit, Google News custom).
/// The collector picks the adapter by <see cref="NewsSourceEntity.Kind"/>.
/// </summary>
public interface INewsAdapter
{
    /// <summary>Discriminator that matches NewsSourceEntity.Kind ("rss", …).</summary>
    string Kind { get; }

    Task<IReadOnlyList<NewsItemDto>> FetchAsync(
        string url,
        CancellationToken cancellationToken = default);
}

public sealed class NewsItemDto
{
    public string? ExternalId { get; set; }
    public string Url { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Summary { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public string? Language { get; set; }
}
