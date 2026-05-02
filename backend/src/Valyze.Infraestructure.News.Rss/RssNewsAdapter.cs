using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.Extensions.Logging;
using Valyze.Domain.Application.News;

namespace Valyze.Infraestructure.News.Rss;

/// <summary>
/// Universal RSS/Atom adapter. Covers Yahoo Finance per-ticker feeds, Google
/// News query feeds, Reddit RSS, SEC EDGAR Atom feeds, and any well-formed
/// publisher feed — that's why we lean on RSS as the v1 transport: free,
/// designed to be polled, no API keys, no ToS friction.
///
/// We do NOT fetch full article bodies. The feed's title + summary is what
/// the AI sees; deep-linking to the URL keeps copyright clean.
/// </summary>
public sealed class RssNewsAdapter : INewsAdapter
{
    public const string KindKey = "rss";
    public const string HttpClientName = "valyze-news-rss";

    private readonly IHttpClientFactory _factory;
    private readonly ILogger<RssNewsAdapter> _logger;

    public RssNewsAdapter(IHttpClientFactory factory, ILogger<RssNewsAdapter> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public string Kind => KindKey;

    public async Task<IReadOnlyList<NewsItemDto>> FetchAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var client = _factory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("RSS fetch returned {Status} for {Url}", response.StatusCode, url);
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        // CheckCharacters=false: many feeds in the wild contain entities that
        // SyndicationFeed.Load chokes on otherwise. Tradeoff is we pass through
        // a few oddball control chars; harmless given we re-serialise titles
        // as plain strings later.
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore,
            CheckCharacters = false,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        });

        SyndicationFeed feed;
        try
        {
            feed = SyndicationFeed.Load(reader);
        }
        catch (XmlException ex)
        {
            _logger.LogWarning(ex, "Could not parse feed at {Url}", url);
            return [];
        }

        var language = feed.Language;
        var items = new List<NewsItemDto>();
        foreach (var item in feed.Items)
        {
            var canonicalUrl = ExtractCanonicalUrl(item);
            if (string.IsNullOrEmpty(canonicalUrl)) continue;

            var title = item.Title?.Text?.Trim();
            if (string.IsNullOrEmpty(title)) continue;

            var summary = item.Summary?.Text?.Trim();
            // Atom feeds expose Content where RSS uses Summary — fall back when needed.
            if (string.IsNullOrEmpty(summary) && item.Content is TextSyndicationContent text)
            {
                summary = text.Text?.Trim();
            }

            var publishedAt = item.PublishDate == default
                ? (item.LastUpdatedTime == default ? DateTimeOffset.UtcNow : item.LastUpdatedTime)
                : item.PublishDate;

            items.Add(new NewsItemDto
            {
                // ExternalId is sometimes a long URL — clamp to fit the column.
                ExternalId = string.IsNullOrEmpty(item.Id)
                    ? null
                    : (item.Id.Length > 256 ? item.Id[..256] : item.Id),
                // Url field is also bounded; some Google News redirect URLs blow past 1KB.
                Url = canonicalUrl.Length > 1024 ? canonicalUrl[..1024] : canonicalUrl,
                Title = NormalizeForDb(title, 512)!,
                Summary = NormalizeForDb(summary, 4000),
                PublishedAt = publishedAt,
                Language = language,
            });
        }

        return items;
    }

    private static string? ExtractCanonicalUrl(SyndicationItem item)
    {
        // Prefer the alternate link (the human-readable article URL); fall
        // back to whatever else the feed exposes (some feeds only set Id).
        var alternate = item.Links.FirstOrDefault(l =>
            string.Equals(l.RelationshipType, "alternate", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(l.RelationshipType));
        if (alternate?.Uri is { } u) return u.ToString();
        if (item.Links.Count > 0 && item.Links[0].Uri is { } first) return first.ToString();
        if (Uri.TryCreate(item.Id, UriKind.Absolute, out var asUri)) return asUri.ToString();
        return null;
    }

    /// <summary>
    /// Trim + clamp to the column length and strip embedded HTML tags so the
    /// summary the model reads is plain text. Naive regex-free strip is fine
    /// for headline summaries — we don't store full bodies.
    /// </summary>
    private static string? NormalizeForDb(string? input, int max)
    {
        if (string.IsNullOrEmpty(input)) return null;
        var stripped = StripHtml(input).Trim();
        if (stripped.Length == 0) return null;
        return stripped.Length > max ? stripped[..max] : stripped;
    }

    private static string StripHtml(string s)
    {
        if (s.IndexOf('<') < 0) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        bool inTag = false;
        foreach (var c in s)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }
}
