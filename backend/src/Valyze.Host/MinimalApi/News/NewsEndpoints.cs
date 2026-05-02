using Valyze.Domain.Application.News;
using Valyze.Domain.Entities.Identity;
using Valyze.Domain.Entities.News;
using Valyze.Domain.Enum;

namespace Valyze.Host.MinimalApi.News;

public static class NewsEndpoints
{
    public static RouteGroupBuilder MapNewsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/news")
            .WithTags("News")
            .RequireAuthorization();

        // Latest articles tagged against the user's holdings.
        group.MapGet("/", async (
            IGetLatestNewsUseCase useCase,
            AccessorClassEntity accessor,
            int? limit,
            DateTimeOffset? since,
            string? symbol,
            IGetNewsForSymbolUseCase symbolUseCase,
            CancellationToken ct) =>
        {
            var resolvedLimit = limit ?? 25;
            IReadOnlyList<NewsArticleEntity> articles = string.IsNullOrEmpty(symbol)
                ? await useCase.ExecuteAsync(accessor.AccountId, since, resolvedLimit, ct)
                : await symbolUseCase.ExecuteAsync(symbol, since, resolvedLimit, ct);

            return Results.Ok(new
            {
                count = articles.Count,
                articles = articles.Select(ToArticle).ToArray(),
            });
        }).WithName("GetNews");

        group.MapGet("/sources/", async (
            IListNewsSourcesUseCase useCase,
            bool? includeDisabled,
            CancellationToken ct) =>
        {
            var sources = await useCase.ExecuteAsync(includeDisabled ?? false, ct);
            return Results.Ok(new { count = sources.Count, sources = sources.Select(ToSource).ToArray() });
        }).WithName("ListNewsSources");

        group.MapPost("/sources/", async (
            IAddNewsSourceUseCase useCase,
            AddNewsSourceBody body,
            CancellationToken ct) =>
        {
            var created = await useCase.ExecuteAsync(new AddNewsSourceCommand
            {
                Name = body.Name,
                Kind = body.Kind ?? "rss",
                UrlTemplate = body.UrlTemplate,
                Scope = body.Scope ?? NewsSourceScope.PerSymbol,
                PollingIntervalMinutes = body.PollingIntervalMinutes ?? 30,
            }, ct);
            return Results.Ok(ToSource(created));
        }).WithName("AddNewsSource");

        group.MapPost("/sources/{id:guid}/disable", async (
            IDisableNewsSourceUseCase useCase,
            Guid id,
            CancellationToken ct) =>
        {
            await useCase.ExecuteAsync(id, ct);
            return Results.NoContent();
        }).WithName("DisableNewsSource");

        // Manual refresh — same path the BackgroundService takes, just on demand.
        group.MapPost("/refresh", async (
            IRefreshNewsUseCase useCase,
            CancellationToken ct) =>
        {
            var result = await useCase.ExecuteAsync(ct);
            return Results.Ok(new
            {
                sourcesPolled = result.SourcesPolled,
                articlesAdded = result.ArticlesAdded,
                warnings = result.Warnings,
            });
        }).WithName("RefreshNews");

        return group;
    }

    public sealed class AddNewsSourceBody
    {
        public string Name { get; set; } = null!;
        public string? Kind { get; set; }
        public string UrlTemplate { get; set; } = null!;
        public NewsSourceScope? Scope { get; set; }
        public int? PollingIntervalMinutes { get; set; }
    }

    private static object ToArticle(NewsArticleEntity a) => new
    {
        id = a.Id,
        sourceId = a.SourceId,
        url = a.Url,
        title = a.Title,
        summary = a.Summary,
        publishedAt = a.PublishedAt,
        fetchedAt = a.FetchedAt,
        language = a.Language,
        instruments = a.Instruments,
    };

    private static object ToSource(NewsSourceEntity s) => new
    {
        id = s.Id,
        name = s.Name,
        kind = s.Kind,
        urlTemplate = s.UrlTemplate,
        scope = s.Scope.ToString(),
        pollingIntervalMinutes = s.PollingIntervalMinutes,
        enabled = s.Enabled,
        createdAt = s.CreatedAt,
        lastPolledAt = s.LastPolledAt,
        lastError = s.LastError,
    };
}
