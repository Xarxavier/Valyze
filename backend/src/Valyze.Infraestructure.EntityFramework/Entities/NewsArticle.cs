namespace Valyze.Infraestructure.EntityFramework.Entities;

public sealed class NewsArticle
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public string? ExternalId { get; set; }
    public string Url { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Summary { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public string? Language { get; set; }
}
