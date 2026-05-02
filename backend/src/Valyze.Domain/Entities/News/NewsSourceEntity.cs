using Valyze.Domain.Enum;

namespace Valyze.Domain.Entities.News;

/// <summary>
/// A configured news feed Valyze polls. Sources are global (operator-managed,
/// shared across users in self-hosted personal mode) — there's no AccountId
/// here on purpose. Adding/removing happens via use cases (and via MCP tools
/// so the AI assistant can curate them).
///
/// Only RSS/Atom feeds are supported in v1 — see <see cref="Kind"/>. RSS is
/// designed to be polled, so respecting <see cref="PollingIntervalMinutes"/>
/// keeps the operator out of every publisher's ban-zone with zero cost.
/// </summary>
public sealed class NewsSourceEntity
{
    public Guid Id { get; set; }

    /// <summary>Human-readable name shown in UI / MCP responses.</summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Adapter discriminator. v1 only honours <c>"rss"</c>; future kinds
    /// (e.g. <c>"json-feed"</c>, <c>"reddit"</c>) plug in their own adapter
    /// implementations on the same row shape.
    /// </summary>
    public string Kind { get; set; } = "rss";

    /// <summary>
    /// URL template. Supports two placeholders:
    ///   <c>{symbol}</c> → raw instrument symbol (ISIN or ticker, URL-encoded)
    ///   <c>{name}</c>   → friendly instrument name (URL-encoded), falls back to symbol when null
    /// For <see cref="NewsSourceScope.Global"/> sources the template is fetched verbatim.
    /// </summary>
    public string UrlTemplate { get; set; } = null!;

    public NewsSourceScope Scope { get; set; }

    public int PollingIntervalMinutes { get; set; } = 30;

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last time the collector polled this source (any expansion).</summary>
    public DateTimeOffset? LastPolledAt { get; set; }

    /// <summary>Last error message from the collector — null on success.</summary>
    public string? LastError { get; set; }
}
