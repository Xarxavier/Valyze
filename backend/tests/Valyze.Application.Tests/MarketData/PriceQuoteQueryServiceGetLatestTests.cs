using Xunit;

namespace Valyze.Application.Tests.MarketData;

/// <summary>
/// Behaviour tests for IPriceQuoteQueryService.GetLatestForSymbolAsync.
///
/// Real assertions require a live Postgres connection. Both tests are
/// skipped until Testcontainers infra is wired up (same gate as
/// TrackRecordSqlVsUseCaseTests). The skip messages document exactly
/// what should be asserted when the infra lands.
/// </summary>
public sealed class PriceQuoteQueryServiceGetLatestTests
{
    [Fact(Skip = "Integration test — requires Postgres Testcontainers (deferred). " +
                 "TODO: seed a price_quotes row for 'AAPL' and assert the returned " +
                 "Money is non-null with the correct amount and currency.")]
    public async Task GetLatestForSymbolAsync_returns_money_when_quote_exists()
    {
        // TODO (Testcontainers):
        // 1. Seed price_quotes: symbol='AAPL', currency='USD', amount=185.50, fetched_at=now()
        // 2. Call sut.GetLatestForSymbolAsync("AAPL", CancellationToken.None)
        // 3. Assert: result != null && result.Value.Amount == 185.50m && result.Value.Currency.Code == "USD"
        await Task.CompletedTask;
    }

    [Fact(Skip = "Integration test — requires Postgres Testcontainers (deferred). " +
                 "TODO: ensure price_quotes has no row for the queried symbol " +
                 "and assert the returned Money? is null.")]
    public async Task GetLatestForSymbolAsync_returns_null_when_no_quote_exists()
    {
        // TODO (Testcontainers):
        // 1. Ensure no price_quotes row with symbol='UNKNOWN_SYMBOL_XYZ'
        // 2. Call sut.GetLatestForSymbolAsync("UNKNOWN_SYMBOL_XYZ", CancellationToken.None)
        // 3. Assert: result == null
        await Task.CompletedTask;
    }
}
