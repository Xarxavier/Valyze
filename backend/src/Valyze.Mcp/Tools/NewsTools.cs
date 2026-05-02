using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Valyze.Mcp.Backend;

namespace Valyze.Mcp.Tools;

/// <summary>
/// MCP tools for the news subsystem. These give the AI:
///   - Read access (get_news_for_symbol, get_latest_news) for analysis
///   - Source curation (list_news_sources, add_news_source, disable_news_source)
///   - Manual ingestion trigger (refresh_news)
///
/// Source management is intentional: the user wants the AI to be able to
/// shape its own data feed without leaving the chat. Adding a source is just
/// inserting a row, so it's safe and reversible.
/// </summary>
[McpServerToolType]
public static class NewsTools
{
    [McpServerTool(Name = "get_news_for_symbol")]
    [Description(
        "Returns recent news articles tagged against the given instrument (ISIN like " +
        "US88160R1014, or ticker like BTC). Use this when the user asks about news, " +
        "catalysts, or context for a specific holding. Articles are tagged via " +
        "case-insensitive matching against the instrument name + symbol — recall is " +
        "decent but not perfect. Optional `since` (ISO 8601) limits to recent articles; " +
        "`limit` defaults to 25, max 200.")]
    public static async Task<string> GetNewsForSymbolAsync(
        ValyzeApiClient client,
        [Description("ISIN or ticker, case-insensitive.")] string symbol,
        [Description("ISO 8601 lower bound on publishedAt. Omit for no lower bound.")] string? since,
        [Description("Max articles. Default 25, range 1..200.")] int? limit,
        CancellationToken cancellationToken)
    {
        var query = $"/api/news/?symbol={Uri.EscapeDataString(symbol)}";
        if (!string.IsNullOrEmpty(since)) query += $"&since={Uri.EscapeDataString(since)}";
        if (limit.HasValue) query += $"&limit={limit.Value}";
        return await client.GetJsonAsync(query, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "get_latest_news")]
    [Description(
        "Returns the latest news articles across the user's whole portfolio (any " +
        "instrument they currently hold). Use this for daily catch-up queries or to " +
        "summarise what's been happening lately. Optional `since` and `limit` work " +
        "the same as get_news_for_symbol.")]
    public static async Task<string> GetLatestNewsAsync(
        ValyzeApiClient client,
        [Description("ISO 8601 lower bound on publishedAt. Omit for no lower bound.")] string? since,
        [Description("Max articles. Default 25, range 1..200.")] int? limit,
        CancellationToken cancellationToken)
    {
        var query = "/api/news/";
        var hasParam = false;
        if (!string.IsNullOrEmpty(since)) { query += "?since=" + Uri.EscapeDataString(since); hasParam = true; }
        if (limit.HasValue) { query += (hasParam ? "&" : "?") + "limit=" + limit.Value; }
        return await client.GetJsonAsync(query, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "list_news_sources")]
    [Description(
        "Lists configured news sources. Each entry has id, name, kind (rss for v1), " +
        "urlTemplate, scope (PerSymbol expands {symbol}/{name}; Global is fetched " +
        "verbatim), pollingIntervalMinutes, enabled, lastPolledAt, lastError. Use " +
        "before adding/disabling so you don't duplicate.")]
    public static async Task<string> ListNewsSourcesAsync(
        ValyzeApiClient client,
        [Description("Set to true to include disabled sources. Default false.")] bool? includeDisabled,
        CancellationToken cancellationToken)
    {
        var query = "/api/news/sources/";
        if (includeDisabled == true) query += "?includeDisabled=true";
        return await client.GetJsonAsync(query, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "add_news_source")]
    [Description(
        "Adds a new news source the collector should poll. v1 only supports RSS/Atom " +
        "feeds (`kind=\"rss\"`). The URL template can include `{name}` (friendly name) " +
        "and `{symbol}` (ISIN/ticker) placeholders — both are URL-encoded automatically. " +
        "Use scope=\"PerSymbol\" for per-instrument feeds (Yahoo Finance per ticker, " +
        "Google News query) and scope=\"Global\" for one-shot feeds (Reuters business " +
        "wire, Bloomberg homepage). Polling interval must be >= 5 minutes — be polite " +
        "with publishers, free feeds work because we behave like proper RSS readers.")]
    public static async Task<string> AddNewsSourceAsync(
        ValyzeApiClient client,
        [Description("Human-readable label.")] string name,
        [Description("RSS feed URL template. Use {name} and/or {symbol} placeholders.")] string urlTemplate,
        [Description("PerSymbol or Global. Default PerSymbol.")] string? scope,
        [Description("Polling interval in minutes. Default 30, minimum 5.")] int? pollingIntervalMinutes,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            name,
            kind = "rss",
            urlTemplate,
            scope = scope ?? "PerSymbol",
            pollingIntervalMinutes = pollingIntervalMinutes ?? 30,
        });
        return await client.PostJsonAsync("/api/news/sources/", body, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "disable_news_source")]
    [Description(
        "Disables a news source by id. The row stays in the database (re-enable manually " +
        "via SQL or a future endpoint), but the collector stops polling it.")]
    public static async Task<string> DisableNewsSourceAsync(
        ValyzeApiClient client,
        [Description("Source id (UUID).")] string sourceId,
        CancellationToken cancellationToken)
    {
        await client.PostJsonAsync($"/api/news/sources/{Uri.EscapeDataString(sourceId)}/disable", body: null, cancellationToken)
            .ConfigureAwait(false);
        return "{\"ok\":true}";
    }

    [McpServerTool(Name = "refresh_news")]
    [Description(
        "Forces an immediate poll of every enabled source. Use when the user asks for " +
        "up-to-the-minute headlines (\"refrescá\", \"hace un check ahora\"); otherwise " +
        "the BackgroundService polls every 30 min by default. Returns sourcesPolled and " +
        "articlesAdded counts.")]
    public static async Task<string> RefreshNewsAsync(
        ValyzeApiClient client,
        CancellationToken cancellationToken)
    {
        return await client.PostJsonAsync("/api/news/refresh", body: null, cancellationToken).ConfigureAwait(false);
    }
}
