namespace Valyze.Domain.Entities.News;

/// <summary>
/// A single article fetched from a news source. Identity for dedup is the
/// canonical <see cref="Url"/> — the same article surfaced by two feeds is
/// stored once. <see cref="ExternalId"/> is the publisher's own GUID when
/// the feed provides one; we keep it for traceability but don't dedupe on it
/// because two feeds wrap the same article in different GUIDs.
///
/// Articles are NOT scoped per account: the same article applies to every
/// user holding the mentioned instrument. <see cref="NewsArticleInstrumentEntity"/>
/// links each article to one or more instruments via simple text matching.
/// </summary>
public sealed class NewsArticleEntity
{
    public Guid Id { get; set; }

    public Guid SourceId { get; set; }

    public string? ExternalId { get; set; }

    public string Url { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Summary { get; set; }

    public DateTimeOffset PublishedAt { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>Best-effort BCP-47 language tag from the feed (e.g. "en", "es").</summary>
    public string? Language { get; set; }

    /// <summary>Instruments this article was tagged against. Populated on read.</summary>
    public IReadOnlyList<string> Instruments { get; set; } = [];
}
