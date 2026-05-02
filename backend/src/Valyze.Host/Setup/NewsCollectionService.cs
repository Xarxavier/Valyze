using Valyze.Domain.Application.News;

namespace Valyze.Host.Setup;

/// <summary>
/// Periodic news collector. Ticks every 5 minutes; the per-source interval
/// guard inside <see cref="IRefreshNewsUseCase"/> decides which feeds are
/// actually due. Stays as a BackgroundService rather than a Hangfire job
/// because it's a single periodic loop with no fan-out — Hangfire arrives
/// later for the AI suggestion pipeline where its retry/dashboard story
/// pays off.
/// </summary>
public sealed class NewsCollectionService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NewsCollectionService> _logger;

    public NewsCollectionService(
        IServiceScopeFactory scopeFactory,
        ILogger<NewsCollectionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("News collector starting; tick every {Interval}.", TickInterval);

        // Initial delay so we don't compete with startup migrations / seed.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TickInterval);
        do
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var refresh = scope.ServiceProvider.GetRequiredService<IRefreshNewsUseCase>();
                var result = await refresh.ExecuteAsync(stoppingToken).ConfigureAwait(false);
                if (result.SourcesPolled > 0 || result.ArticlesAdded > 0)
                {
                    _logger.LogInformation(
                        "News tick: polled {Polled} sources, added {Added} articles.",
                        result.SourcesPolled, result.ArticlesAdded);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Surface the failure but keep the loop alive — most failures
                // are transient (publisher 503s, DNS hiccups).
                _logger.LogError(ex, "News collector tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
