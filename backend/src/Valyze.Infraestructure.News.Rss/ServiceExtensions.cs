using Microsoft.Extensions.DependencyInjection;
using Valyze.Domain.Application.News;

namespace Valyze.Infraestructure.News.Rss;

public static class ServiceExtensions
{
    public static IServiceCollection AddValyzeNewsRss(this IServiceCollection services)
    {
        services.AddHttpClient(RssNewsAdapter.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            // Polite UA: identifies us so publishers can rate-limit cleanly,
            // not stealth so we don't end up on a block-list. Some RSS hosts
            // (Google News among them) reject the default .NET UA outright.
            client.DefaultRequestHeaders.Add("User-Agent",
                "valyze/0.1 (+https://github.com/Xarxavier/Valyze) news-collector");
            client.DefaultRequestHeaders.Add("Accept", "application/rss+xml,application/atom+xml,application/xml,text/xml,*/*;q=0.5");
        });

        services.AddScoped<INewsAdapter, RssNewsAdapter>();
        return services;
    }
}
