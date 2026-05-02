using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Valyze.Domain.Application.News;
using Valyze.Domain.Entities.News;
using Valyze.Domain.Enum;
using Valyze.Domain.QueryService;
using Valyze.Domain.Repository;

namespace Valyze.Application.News;

/// <summary>
/// Polls every enabled news source and persists new articles. The same
/// orchestration runs on the periodic <c>BackgroundService</c> tick AND on
/// manual triggers (REST + MCP). The interval guard is applied here so
/// callers don't need to know about it.
/// </summary>
public class RefreshNewsUseCase : IRefreshNewsUseCase
{
    private readonly INewsSourceQueryService _sourceQuery;
    private readonly INewsArticleQueryService _articleQuery;
    private readonly INewsSourceRepository _sourceRepo;
    private readonly INewsArticleRepository _articleRepo;
    private readonly IEnumerable<INewsAdapter> _adapters;
    private readonly ILogger<RefreshNewsUseCase> _logger;

    public RefreshNewsUseCase(
        INewsSourceQueryService sourceQuery,
        INewsArticleQueryService articleQuery,
        INewsSourceRepository sourceRepo,
        INewsArticleRepository articleRepo,
        IEnumerable<INewsAdapter> adapters,
        ILogger<RefreshNewsUseCase> logger)
    {
        _sourceQuery = sourceQuery;
        _articleQuery = articleQuery;
        _sourceRepo = sourceRepo;
        _articleRepo = articleRepo;
        _adapters = adapters;
        _logger = logger;
    }

    public async Task<RefreshNewsResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var sources = await _sourceQuery.ListAsync(includeDisabled: false, cancellationToken);
        var tracked = await _articleQuery.GetTrackedInstrumentsAsync(cancellationToken);
        var adaptersByKind = _adapters.ToDictionary(
            a => a.Kind,
            StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        int sourcesPolled = 0;
        int articlesAdded = 0;
        var warnings = new List<string>();

        foreach (var source in sources)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Honour the per-source interval. The collector ticks more often
            // than any single feed needs to, so most sources are skipped on
            // most ticks.
            if (source.LastPolledAt is not null
                && (now - source.LastPolledAt.Value).TotalMinutes < source.PollingIntervalMinutes)
            {
                continue;
            }

            if (!adaptersByKind.TryGetValue(source.Kind, out var adapter))
            {
                warnings.Add($"No adapter registered for kind '{source.Kind}' (source '{source.Name}')");
                continue;
            }

            sourcesPolled++;
            var fetchedArticles = new List<NewsArticleEntity>();
            string? error = null;

            try
            {
                var urls = ExpandUrls(source, tracked);
                foreach (var (url, instrumentHint) in urls)
                {
                    var items = await adapter.FetchAsync(url, cancellationToken);
                    foreach (var item in items)
                    {
                        fetchedArticles.Add(new NewsArticleEntity
                        {
                            SourceId = source.Id,
                            ExternalId = item.ExternalId,
                            Url = item.Url,
                            Title = item.Title,
                            Summary = item.Summary,
                            PublishedAt = item.PublishedAt,
                            FetchedAt = now,
                            Language = item.Language,
                            Instruments = instrumentHint is null ? [] : [instrumentHint],
                        });
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                error = ex.Message;
                _logger.LogWarning(ex, "News source '{Source}' failed", source.Name);
            }

            if (fetchedArticles.Count > 0)
            {
                var inserted = await _articleRepo.UpsertManyAsync(fetchedArticles, cancellationToken);

                // Build the article-id → hinted-instrument lookup before we lose
                // the per-fetch context. The same article may be hinted by
                // multiple instruments if a global feed mentions several;
                // we union them.
                var hintedByUrl = fetchedArticles
                    .GroupBy(a => a.Url, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.SelectMany(a => a.Instruments).ToHashSet(StringComparer.OrdinalIgnoreCase),
                        StringComparer.OrdinalIgnoreCase);

                foreach (var article in inserted)
                {
                    var tags = TagInstruments(article, tracked, hintedByUrl);
                    if (tags.Count > 0)
                    {
                        await _articleRepo.TagInstrumentsAsync(article.Id, tags, cancellationToken);
                    }
                }
                articlesAdded += inserted.Count;
            }

            await _sourceRepo.UpdatePollingStateAsync(source.Id, now, error, cancellationToken);
        }

        _logger.LogInformation(
            "News refresh: polled {Polled}/{Total} sources, added {Added} articles",
            sourcesPolled, sources.Count, articlesAdded);

        return new RefreshNewsResult
        {
            SourcesPolled = sourcesPolled,
            ArticlesAdded = articlesAdded,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Expands a per-symbol source's URL template once per held instrument
    /// (yielding a hint that lets us pre-tag the article without scanning).
    /// Global sources fetch the URL verbatim and rely on text matching.
    /// </summary>
    private static IEnumerable<(string Url, string? InstrumentHint)> ExpandUrls(
        NewsSourceEntity source,
        IReadOnlyList<(string Instrument, string? Name)> tracked)
    {
        if (source.Scope == NewsSourceScope.Global)
        {
            yield return (source.UrlTemplate, null);
            yield break;
        }

        foreach (var (instrument, name) in tracked)
        {
            // For the Google-News-style query=name template we want the
            // friendly name; for ticker templates ({symbol}) we want the
            // raw symbol. Both substitutions are URL-encoded.
            var query = string.IsNullOrEmpty(name) ? instrument : name;
            var url = source.UrlTemplate
                .Replace("{name}", Uri.EscapeDataString(query), StringComparison.Ordinal)
                .Replace("{symbol}", Uri.EscapeDataString(instrument), StringComparison.Ordinal);
            yield return (url, instrument);
        }
    }

    /// <summary>
    /// Case-insensitive contains-match of every tracked (name, instrument)
    /// against title + summary. v1 is intentionally simple: free, fast, no
    /// LLM calls. False-positives on common names (e.g. "BIT" inside a word)
    /// are filtered with a word-boundary check via Regex.
    /// </summary>
    private static List<NewsArticleInstrumentEntity> TagInstruments(
        NewsArticleEntity article,
        IReadOnlyList<(string Instrument, string? Name)> tracked,
        IReadOnlyDictionary<string, HashSet<string>> hintedByUrl)
    {
        var tags = new List<NewsArticleInstrumentEntity>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (hintedByUrl.TryGetValue(article.Url, out var hinted))
        {
            foreach (var inst in hinted)
            {
                if (seen.Add(inst))
                    tags.Add(new NewsArticleInstrumentEntity { Instrument = inst, Confidence = 1.0 });
            }
        }

        var haystack = $"{article.Title}\n{article.Summary}";
        foreach (var (instrument, name) in tracked)
        {
            if (seen.Contains(instrument)) continue;

            // Try the friendly name first (more discriminating); fall back to the symbol.
            // Skip very-short names to avoid garbage matches like "BTC" in "QBTCO".
            var candidates = new[] { name, instrument }.Where(c => !string.IsNullOrEmpty(c) && c!.Length >= 3);
            foreach (var candidate in candidates)
            {
                if (Regex.IsMatch(
                        haystack,
                        @"\b" + Regex.Escape(candidate!) + @"\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    tags.Add(new NewsArticleInstrumentEntity { Instrument = instrument, Confidence = 0.7 });
                    seen.Add(instrument);
                    break;
                }
            }
        }

        return tags;
    }
}
