using Valyze.Domain.Entities.News;
using Valyze.Domain.Enum;

namespace Valyze.Domain.Application.News;

public interface IListNewsSourcesUseCase
{
    Task<IReadOnlyList<NewsSourceEntity>> ExecuteAsync(
        bool includeDisabled,
        CancellationToken cancellationToken = default);
}

public interface IAddNewsSourceUseCase
{
    Task<NewsSourceEntity> ExecuteAsync(
        AddNewsSourceCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class AddNewsSourceCommand
{
    public string Name { get; set; } = null!;
    public string Kind { get; set; } = "rss";
    public string UrlTemplate { get; set; } = null!;
    public NewsSourceScope Scope { get; set; } = NewsSourceScope.PerSymbol;
    public int PollingIntervalMinutes { get; set; } = 30;
}

public interface IDisableNewsSourceUseCase
{
    Task ExecuteAsync(Guid sourceId, CancellationToken cancellationToken = default);
}
