using Valyze.Domain.Entities.News;

namespace Valyze.Domain.QueryService;

public interface INewsSourceQueryService
{
    Task<IReadOnlyList<NewsSourceEntity>> ListAsync(
        bool includeDisabled,
        CancellationToken cancellationToken = default);
}
