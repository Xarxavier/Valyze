using Dapper;
using Microsoft.Extensions.Configuration;
using Valyze.Domain.Entities.News;
using Valyze.Domain.Enum;
using Valyze.Domain.QueryService;

namespace Valyze.Infraestructure.QueryService.News;

public class NewsSourceQueryService : BaseQueryService, INewsSourceQueryService
{
    public NewsSourceQueryService(IConfiguration configuration) : base(configuration) { }

    private sealed record SourceRow(
        Guid Id,
        string Name,
        string Kind,
        string UrlTemplate,
        short Scope,
        int PollingIntervalMinutes,
        bool Enabled,
        DateTime CreatedAt,
        DateTime? LastPolledAt,
        string? LastError);

    public async Task<IReadOnlyList<NewsSourceEntity>> ListAsync(
        bool includeDisabled,
        CancellationToken cancellationToken = default)
    {
        using var conn = CreateConnection();
        var sql = @"
            SELECT
                id                       AS Id,
                name                     AS Name,
                kind                     AS Kind,
                url_template             AS UrlTemplate,
                scope                    AS Scope,
                polling_interval_minutes AS PollingIntervalMinutes,
                enabled                  AS Enabled,
                created_at               AS CreatedAt,
                last_polled_at           AS LastPolledAt,
                last_error               AS LastError
            FROM news_sources"
            + (includeDisabled ? "" : " WHERE enabled = TRUE")
            + " ORDER BY created_at;";

        var rows = await conn.QueryAsync<SourceRow>(sql);
        return rows
            .Select(r => new NewsSourceEntity
            {
                Id = r.Id,
                Name = r.Name,
                Kind = r.Kind,
                UrlTemplate = r.UrlTemplate,
                Scope = (NewsSourceScope)r.Scope,
                PollingIntervalMinutes = r.PollingIntervalMinutes,
                Enabled = r.Enabled,
                CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc)),
                LastPolledAt = r.LastPolledAt is null
                    ? null
                    : new DateTimeOffset(DateTime.SpecifyKind(r.LastPolledAt.Value, DateTimeKind.Utc)),
                LastError = r.LastError,
            })
            .ToList();
    }
}
