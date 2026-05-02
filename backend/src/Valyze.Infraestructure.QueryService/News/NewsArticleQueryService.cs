using Dapper;
using Microsoft.Extensions.Configuration;
using Valyze.Domain.Entities.News;
using Valyze.Domain.QueryService;

namespace Valyze.Infraestructure.QueryService.News;

public class NewsArticleQueryService : BaseQueryService, INewsArticleQueryService
{
    public NewsArticleQueryService(IConfiguration configuration) : base(configuration) { }

    private sealed record ArticleRow(
        Guid Id,
        Guid SourceId,
        string? ExternalId,
        string Url,
        string Title,
        string? Summary,
        DateTime PublishedAt,
        DateTime FetchedAt,
        string? Language);

    public async Task<IReadOnlyList<NewsArticleEntity>> GetForSymbolAsync(
        string instrument,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        // Casting @Since to timestamptz keeps Npgsql happy when the param is
        // null — without the cast it can't infer the column type and aborts.
        var sql = @"
            SELECT
                a.id           AS Id,
                a.source_id    AS SourceId,
                a.external_id  AS ExternalId,
                a.url          AS Url,
                a.title        AS Title,
                a.summary      AS Summary,
                a.published_at AS PublishedAt,
                a.fetched_at   AS FetchedAt,
                a.language     AS Language
            FROM news_articles a
            JOIN news_article_instruments t ON t.article_id = a.id
            WHERE LOWER(t.instrument) = LOWER(@Instrument)
              AND (CAST(@Since AS timestamptz) IS NULL OR a.published_at >= CAST(@Since AS timestamptz))
            ORDER BY a.published_at DESC
            LIMIT @Limit;";

        var rows = await conn.QueryAsync<ArticleRow>(sql, new
        {
            Instrument = instrument,
            Since = since?.UtcDateTime,
            Limit = limit,
        });
        return await AttachInstrumentsAsync(conn, rows.ToList());
    }

    public async Task<IReadOnlyList<NewsArticleEntity>> GetLatestForAccountAsync(
        Guid accountId,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        var sql = @"
            SELECT DISTINCT
                a.id           AS Id,
                a.source_id    AS SourceId,
                a.external_id  AS ExternalId,
                a.url          AS Url,
                a.title        AS Title,
                a.summary      AS Summary,
                a.published_at AS PublishedAt,
                a.fetched_at   AS FetchedAt,
                a.language     AS Language
            FROM news_articles a
            JOIN news_article_instruments t ON t.article_id = a.id
            WHERE EXISTS (
                SELECT 1 FROM trades tr
                WHERE tr.account_id = @AccountId
                  AND LOWER(tr.instrument) = LOWER(t.instrument))
              AND (CAST(@Since AS timestamptz) IS NULL OR a.published_at >= CAST(@Since AS timestamptz))
            ORDER BY a.published_at DESC
            LIMIT @Limit;";

        var rows = await conn.QueryAsync<ArticleRow>(sql, new
        {
            AccountId = accountId,
            Since = since?.UtcDateTime,
            Limit = limit,
        });
        return await AttachInstrumentsAsync(conn, rows.ToList());
    }

    public async Task<IReadOnlyList<(string Instrument, string? Name)>> GetTrackedInstrumentsAsync(
        CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        // The tracked set is the distinct instruments held across all accounts
        // (in personal mode there's only one). Friendly name is the most recent
        // non-null name observed in any trade for that instrument.
        const string sql = @"
            SELECT instrument AS Instrument,
                   (SELECT instrument_name
                      FROM trades t2
                     WHERE t2.instrument = t.instrument
                       AND t2.instrument_name IS NOT NULL
                     ORDER BY t2.executed_at DESC
                     LIMIT 1) AS Name
            FROM (SELECT DISTINCT instrument FROM trades) t
            ORDER BY instrument;";

        var rows = await conn.QueryAsync<(string Instrument, string? Name)>(sql);
        return rows.ToList();
    }

    /// <summary>
    /// Pulls the per-article instrument tags in a single round trip and
    /// attaches them to the rows. Done outside the main query so the JOIN
    /// stays clean and we keep the result deduplicated by article.
    /// </summary>
    private static async Task<IReadOnlyList<NewsArticleEntity>> AttachInstrumentsAsync(
        Npgsql.NpgsqlConnection conn,
        IReadOnlyList<ArticleRow> rows)
    {
        if (rows.Count == 0) return [];
        var ids = rows.Select(r => r.Id).Distinct().ToList();
        var tags = (await conn.QueryAsync<(Guid ArticleId, string Instrument)>(
            "SELECT article_id AS ArticleId, instrument AS Instrument FROM news_article_instruments WHERE article_id = ANY(@Ids);",
            new { Ids = ids })).ToList();

        var byArticle = tags
            .GroupBy(t => t.ArticleId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(t => t.Instrument).Distinct().ToList());

        return rows
            .DistinctBy(r => r.Id)
            .Select(r => new NewsArticleEntity
            {
                Id = r.Id,
                SourceId = r.SourceId,
                ExternalId = r.ExternalId,
                Url = r.Url,
                Title = r.Title,
                Summary = r.Summary,
                PublishedAt = new DateTimeOffset(DateTime.SpecifyKind(r.PublishedAt, DateTimeKind.Utc)),
                FetchedAt = new DateTimeOffset(DateTime.SpecifyKind(r.FetchedAt, DateTimeKind.Utc)),
                Language = r.Language,
                Instruments = byArticle.TryGetValue(r.Id, out var insts) ? insts : [],
            })
            .ToList();
    }
}
