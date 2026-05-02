using Valyze.Domain.Application.News;
using Valyze.Domain.Entities.News;
using Valyze.Domain.Exceptions;
using Valyze.Domain.QueryService;
using Valyze.Domain.Repository;

namespace Valyze.Application.News;

public class ListNewsSourcesUseCase : IListNewsSourcesUseCase
{
    private readonly INewsSourceQueryService _query;

    public ListNewsSourcesUseCase(INewsSourceQueryService query)
    {
        _query = query;
    }

    public Task<IReadOnlyList<NewsSourceEntity>> ExecuteAsync(
        bool includeDisabled,
        CancellationToken cancellationToken = default)
        => _query.ListAsync(includeDisabled, cancellationToken);
}

public class AddNewsSourceUseCase : IAddNewsSourceUseCase
{
    private readonly INewsSourceRepository _repo;

    public AddNewsSourceUseCase(INewsSourceRepository repo)
    {
        _repo = repo;
    }

    public Task<NewsSourceEntity> ExecuteAsync(
        AddNewsSourceCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new BusinessException("msnNewsSourceNameRequired");
        if (string.IsNullOrWhiteSpace(command.UrlTemplate))
            throw new BusinessException("msnNewsSourceUrlRequired");
        if (!Uri.TryCreate(
                command.UrlTemplate.Replace("{symbol}", "TEST", StringComparison.Ordinal)
                                   .Replace("{name}", "TEST", StringComparison.Ordinal),
                UriKind.Absolute,
                out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new BusinessException("msnNewsSourceUrlInvalid");
        }
        if (command.PollingIntervalMinutes < 5)
            throw new BusinessException("msnNewsSourceIntervalTooLow");

        var entity = new NewsSourceEntity
        {
            Name = command.Name.Trim(),
            Kind = string.IsNullOrWhiteSpace(command.Kind) ? "rss" : command.Kind.Trim().ToLowerInvariant(),
            UrlTemplate = command.UrlTemplate.Trim(),
            Scope = command.Scope,
            PollingIntervalMinutes = command.PollingIntervalMinutes,
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        return _repo.CreateAsync(entity, cancellationToken);
    }
}

public class DisableNewsSourceUseCase : IDisableNewsSourceUseCase
{
    private readonly INewsSourceRepository _repo;

    public DisableNewsSourceUseCase(INewsSourceRepository repo)
    {
        _repo = repo;
    }

    public Task ExecuteAsync(Guid sourceId, CancellationToken cancellationToken = default)
        => _repo.SetEnabledAsync(sourceId, enabled: false, cancellationToken);
}
