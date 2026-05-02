using Valyze.Domain.Entities.News;
using Valyze.Domain.Enum;

namespace Valyze.Infraestructure.EntityFramework.Mapper;

public static class NewsSourceMapper
{
    public static Entities.NewsSource ToEf(NewsSourceEntity domain) => new()
    {
        Id = domain.Id,
        Name = domain.Name,
        Kind = domain.Kind,
        UrlTemplate = domain.UrlTemplate,
        Scope = (short)domain.Scope,
        PollingIntervalMinutes = domain.PollingIntervalMinutes,
        Enabled = domain.Enabled,
        CreatedAt = domain.CreatedAt,
        LastPolledAt = domain.LastPolledAt,
        LastError = domain.LastError,
    };

    public static NewsSourceEntity ToDomain(Entities.NewsSource ef) => new()
    {
        Id = ef.Id,
        Name = ef.Name,
        Kind = ef.Kind,
        UrlTemplate = ef.UrlTemplate,
        Scope = (NewsSourceScope)ef.Scope,
        PollingIntervalMinutes = ef.PollingIntervalMinutes,
        Enabled = ef.Enabled,
        CreatedAt = ef.CreatedAt,
        LastPolledAt = ef.LastPolledAt,
        LastError = ef.LastError,
    };
}
