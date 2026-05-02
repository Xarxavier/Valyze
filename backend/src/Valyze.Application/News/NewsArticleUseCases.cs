using Valyze.Domain.Application.News;
using Valyze.Domain.Entities.News;
using Valyze.Domain.QueryService;

namespace Valyze.Application.News;

public class GetNewsForSymbolUseCase : IGetNewsForSymbolUseCase
{
    private readonly INewsArticleQueryService _query;

    public GetNewsForSymbolUseCase(INewsArticleQueryService query)
    {
        _query = query;
    }

    public Task<IReadOnlyList<NewsArticleEntity>> ExecuteAsync(
        string instrument,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken = default)
        => _query.GetForSymbolAsync(instrument, since, Clamp(limit), cancellationToken);

    private static int Clamp(int limit) => Math.Clamp(limit <= 0 ? 25 : limit, 1, 200);
}

public class GetLatestNewsUseCase : IGetLatestNewsUseCase
{
    private readonly INewsArticleQueryService _query;

    public GetLatestNewsUseCase(INewsArticleQueryService query)
    {
        _query = query;
    }

    public Task<IReadOnlyList<NewsArticleEntity>> ExecuteAsync(
        Guid accountId,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken = default)
        => _query.GetLatestForAccountAsync(accountId, since, Clamp(limit), cancellationToken);

    private static int Clamp(int limit) => Math.Clamp(limit <= 0 ? 25 : limit, 1, 200);
}
