using Microsoft.Extensions.Options;
using Valyze.Domain.Entities.Identity;
using Valyze.Domain.Entities.News;
using Valyze.Domain.Enum;
using Valyze.Domain.Money;
using Valyze.Domain.QueryService;
using Valyze.Domain.Repository;
using Valyze.Host.Configuration;

namespace Valyze.Host.Setup;

public sealed class SeedRunner
{
    private readonly IAccountRepository _accountRepository;
    private readonly INewsSourceRepository _newsSourceRepository;
    private readonly INewsSourceQueryService _newsSourceQuery;
    private readonly ValyzeOptions _options;
    private readonly ILogger<SeedRunner> _logger;

    public SeedRunner(
        IAccountRepository accountRepository,
        INewsSourceRepository newsSourceRepository,
        INewsSourceQueryService newsSourceQuery,
        IOptions<ValyzeOptions> options,
        ILogger<SeedRunner> logger)
    {
        _accountRepository = accountRepository;
        _newsSourceRepository = newsSourceRepository;
        _newsSourceQuery = newsSourceQuery;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await SeedPersonalAccountAsync(cancellationToken);
        await SeedDefaultNewsSourcesAsync(cancellationToken);
    }

    private async Task SeedPersonalAccountAsync(CancellationToken cancellationToken)
    {
        if (_options.Mode != ValyzeMode.Personal)
        {
            _logger.LogInformation("Mode is {Mode}; skipping personal seed.", _options.Mode);
            return;
        }

        if (await _accountRepository.AnyAsync(cancellationToken))
        {
            _logger.LogInformation("Personal account already seeded.");
            return;
        }

        var account = new AccountEntity
        {
            Id = Guid.NewGuid(),
            Email = _options.Personal.SeedEmail,
            BaseCurrency = new Currency(_options.Personal.BaseCurrency),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _accountRepository.CreateAsync(account, cancellationToken);

        _logger.LogInformation(
            "Seeded personal account {AccountId} ({Email}, base {Currency}).",
            account.Id, account.Email, account.BaseCurrency);
    }

    /// <summary>
    /// First-run seed for the news subsystem. Adds two zero-cost RSS feeds
    /// that work without API keys and are designed to be polled — keeps the
    /// "free, no bans" promise of the v1 ingestion. Operators can disable
    /// these and add their own via the API / MCP later.
    /// </summary>
    private async Task SeedDefaultNewsSourcesAsync(CancellationToken cancellationToken)
    {
        var existing = await _newsSourceQuery.ListAsync(includeDisabled: true, cancellationToken);
        if (existing.Count > 0)
        {
            _logger.LogInformation("News sources already present ({Count}); skipping defaults.", existing.Count);
            return;
        }

        var defaults = new[]
        {
            new NewsSourceEntity
            {
                Id = Guid.NewGuid(),
                Name = "Google News — by name",
                Kind = "rss",
                UrlTemplate = "https://news.google.com/rss/search?q={name}&hl=en-US&gl=US&ceid=US:en",
                Scope = NewsSourceScope.PerSymbol,
                PollingIntervalMinutes = 30,
                Enabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new NewsSourceEntity
            {
                Id = Guid.NewGuid(),
                Name = "Yahoo Finance — by name",
                Kind = "rss",
                UrlTemplate = "https://feeds.finance.yahoo.com/rss/2.0/headline?s={symbol}&region=US&lang=en-US",
                Scope = NewsSourceScope.PerSymbol,
                PollingIntervalMinutes = 30,
                Enabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            },
        };

        foreach (var source in defaults)
        {
            await _newsSourceRepository.CreateAsync(source, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} default news sources.", defaults.Length);
    }
}
