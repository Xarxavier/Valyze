using Valyze.Domain.Entities.News;

namespace Valyze.Infraestructure.EntityFramework.Mapper;

public static class NewsArticleMapper
{
    public static Entities.NewsArticle ToEf(NewsArticleEntity domain) => new()
    {
        Id = domain.Id,
        SourceId = domain.SourceId,
        ExternalId = domain.ExternalId,
        Url = domain.Url,
        Title = domain.Title,
        Summary = domain.Summary,
        PublishedAt = domain.PublishedAt,
        FetchedAt = domain.FetchedAt,
        Language = domain.Language,
    };

    public static NewsArticleEntity ToDomain(Entities.NewsArticle ef) => new()
    {
        Id = ef.Id,
        SourceId = ef.SourceId,
        ExternalId = ef.ExternalId,
        Url = ef.Url,
        Title = ef.Title,
        Summary = ef.Summary,
        PublishedAt = ef.PublishedAt,
        FetchedAt = ef.FetchedAt,
        Language = ef.Language,
    };
}
