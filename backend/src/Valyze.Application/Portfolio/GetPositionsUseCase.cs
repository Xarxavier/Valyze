using Valyze.Domain.Application.MarketData;
using Valyze.Domain.Application.Portfolio;
using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Enum;
using Valyze.Domain.Exceptions;
using Valyze.Domain.Money;
using Valyze.Domain.QueryService;
using Valyze.Domain.Repository;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Application.Portfolio;

public class GetPositionsUseCase : IGetPositionsUseCase
{
    /// <summary>
    /// Cache TTL for live spot prices. Tight enough to track CoinGecko/Yahoo
    /// closely (~30 fetches/hr on a one-user account), but not so tight that
    /// we hammer free APIs on every dashboard reload. The remaining drift vs
    /// the broker's own number is structural — different price sources, not
    /// caching — so making this smaller does NOT close the gap further.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private readonly ITradeQueryService _tradeQueryService;
    private readonly IAccountRepository _accountRepository;
    private readonly IPriceQuoteQueryService _priceQuoteQueryService;
    private readonly IPriceQuoteRepository _priceQuoteRepository;
    private readonly IEnumerable<IPriceFeed> _priceFeeds;
    private readonly IEnumerable<IFxFeed> _fxFeeds;

    public GetPositionsUseCase(
        ITradeQueryService tradeQueryService,
        IAccountRepository accountRepository,
        IPriceQuoteQueryService priceQuoteQueryService,
        IPriceQuoteRepository priceQuoteRepository,
        IEnumerable<IPriceFeed> priceFeeds,
        IEnumerable<IFxFeed> fxFeeds)
    {
        _tradeQueryService = tradeQueryService;
        _accountRepository = accountRepository;
        _priceQuoteQueryService = priceQuoteQueryService;
        _priceQuoteRepository = priceQuoteRepository;
        _priceFeeds = priceFeeds;
        _fxFeeds = fxFeeds;
    }

    public async Task<PositionsViewEntity> ExecuteAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            throw new BusinessException("msnAccountIdRequired");

        var account = await _accountRepository.GetByIdAsync(accountId, cancellationToken)
            ?? throw new BusinessException("msnAccountNotFound");

        var trades = await _tradeQueryService.ListByAccountAsync(accountId, cancellationToken);

        var positions = AggregatePositions(trades);

        // Foreign-currency invested totals (positions denominated in non-base currency).
        // Until FX is wired in, we show them separately rather than summing into base.
        var foreignInvested = positions
            .Where(p => p.AvgCost.Currency != account.BaseCurrency)
            .GroupBy(p => p.AvgCost.Currency)
            .Select(g => new MoneyValue(g.Sum(p => p.TotalCost.Amount), g.Key))
            .ToList();

        var basePositions = positions.Where(p => p.AvgCost.Currency == account.BaseCurrency).ToList();

        var symbolsToPrice = basePositions
            .Where(p => p.Quantity > 0)
            .Select(p => p.Instrument.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var prices = await FetchPricesAsync(symbolsToPrice, account.BaseCurrency, cancellationToken);

        foreach (var p in basePositions)
        {
            if (p.Quantity <= 0) continue;
            if (!prices.TryGetValue(p.Instrument.Value, out var current)) continue;

            // Native price always shown to the user — that's the actual market price.
            // Value & P&L are computed in the account base currency, applying FX when
            // the quote is in a foreign currency.
            p.CurrentPrice = current;

            decimal priceInBase;
            if (current.Currency == account.BaseCurrency)
            {
                priceInBase = current.Amount;
            }
            else
            {
                var rate = await GetFxRateAsync(current.Currency, account.BaseCurrency, cancellationToken);
                if (rate is null) continue; // Can't value without FX — leaves Valued=false.
                priceInBase = current.Amount * rate.Value;
            }

            p.CurrentValue = new MoneyValue(p.Quantity * priceInBase, account.BaseCurrency);
            p.UnrealizedPnl = new MoneyValue(p.CurrentValue.Value.Amount - p.TotalCost.Amount, account.BaseCurrency);
            p.UnrealizedPnlPercent = p.TotalCost.Amount == 0
                ? null
                : Math.Round(p.UnrealizedPnl.Value.Amount / p.TotalCost.Amount * 100m, 4);
            p.Valued = true;

            // Estimated sell commission — derived from the position's last buy fee.
            // For Trade Republic that's a flat 1 EUR per fill. Defaults to 0 in the
            // base currency if the broker's fee is in a different currency we can't
            // convert (we only deduct what we're confident about).
            var lastFee = p.Trades.Count > 0 ? p.Trades[^1].Fees : (MoneyValue?)null;
            if (lastFee is not null && lastFee.Value.Currency == account.BaseCurrency)
            {
                p.EstimatedSellCommission = lastFee.Value;
                p.NetCurrentValue = new MoneyValue(
                    p.CurrentValue.Value.Amount - lastFee.Value.Amount,
                    account.BaseCurrency);
                p.NetUnrealizedPnl = new MoneyValue(
                    p.NetCurrentValue.Value.Amount - p.TotalCost.Amount,
                    account.BaseCurrency);
            }
            else
            {
                // No usable fee data — surface gross value as net (zero deduction).
                p.EstimatedSellCommission = new MoneyValue(0m, account.BaseCurrency);
                p.NetCurrentValue = p.CurrentValue;
                p.NetUnrealizedPnl = p.UnrealizedPnl;
            }
        }

        var totalInvested = new MoneyValue(
            basePositions.Sum(p => p.TotalCost.Amount),
            account.BaseCurrency);
        var totalCurrentValue = new MoneyValue(
            basePositions.Where(p => p.Valued).Sum(p => p.CurrentValue!.Value.Amount),
            account.BaseCurrency);
        var totalUnrealized = new MoneyValue(
            basePositions.Where(p => p.Valued).Sum(p => p.UnrealizedPnl!.Value.Amount),
            account.BaseCurrency);
        var totalRealized = new MoneyValue(
            positions.Where(p => p.RealizedPnl.Currency == account.BaseCurrency)
                     .Sum(p => p.RealizedPnl.Amount),
            account.BaseCurrency);

        var valuedCost = basePositions.Where(p => p.Valued).Sum(p => p.TotalCost.Amount);
        var totalCost = basePositions.Sum(p => p.TotalCost.Amount);
        var coverage = totalCost == 0 ? 1m : valuedCost / totalCost;

        var summary = new PortfolioSummaryEntity
        {
            BaseCurrency = account.BaseCurrency,
            AsOf = DateTimeOffset.UtcNow,
            TotalInvested = totalInvested,
            TotalCurrentValue = totalCurrentValue,
            TotalUnrealizedPnl = totalUnrealized,
            TotalRealizedPnl = totalRealized,
            TotalPnl = new MoneyValue(totalUnrealized.Amount + totalRealized.Amount, account.BaseCurrency),
            OpenPositionCount = positions.Count(p => p.Quantity > 0),
            TradeCount = trades.Count,
            ValuationCoverage = Math.Round(coverage, 4),
            ForeignTotalsInvested = foreignInvested,
        };

        return new PositionsViewEntity
        {
            AccountId = accountId,
            Positions = positions
                .OrderByDescending(p => p.TotalCost.Amount)
                .ToList(),
            Summary = summary,
        };
    }

    private async Task<decimal?> GetFxRateAsync(Currency from, Currency to, CancellationToken ct)
    {
        foreach (var feed in _fxFeeds)
        {
            try
            {
                var rate = await feed.GetRateAsync(from, to, ct);
                if (rate is not null) return rate;
            }
            catch
            {
                // best-effort — try the next feed if one fails
            }
        }
        return null;
    }

    private async Task<Dictionary<string, MoneyValue>> FetchPricesAsync(
        IReadOnlyCollection<string> symbols,
        Currency target,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, MoneyValue>(StringComparer.OrdinalIgnoreCase);
        if (symbols.Count == 0) return result;

        // 1. Read fresh quotes from cache. Anything within the TTL window is reused
        //    verbatim; older or missing entries trigger a feed call below.
        var freshSince = DateTimeOffset.UtcNow - CacheTtl;
        var cached = await _priceQuoteQueryService.GetFreshAsync(symbols, target, freshSince, cancellationToken);
        foreach (var quote in cached)
        {
            result[quote.Symbol] = new MoneyValue(quote.Amount, quote.Currency);
        }

        var missing = symbols
            .Where(s => !result.ContainsKey(s))
            .ToList();
        if (missing.Count == 0) return result;

        // 2. Each registered feed has a chance to satisfy the still-missing symbols.
        //    Feeds are best-effort — a transient outage just leaves rows un-priced.
        var fetchedThisCall = new List<PriceQuoteEntity>();
        var now = DateTimeOffset.UtcNow;

        foreach (var feed in _priceFeeds)
        {
            if (missing.Count == 0) break;
            try
            {
                var fetched = await feed.GetCurrentPricesAsync(missing, target, cancellationToken);
                foreach (var (symbol, price) in fetched)
                {
                    result[symbol] = price;
                    fetchedThisCall.Add(new PriceQuoteEntity
                    {
                        Symbol = symbol.ToUpperInvariant(),
                        Currency = price.Currency,
                        Amount = price.Amount,
                        Source = feed.Provider,
                        FetchedAt = now,
                    });
                }
                missing = missing.Where(s => !result.ContainsKey(s)).ToList();
            }
            catch
            {
                // swallow per-feed failure
            }
        }

        // 3. Persist what we just fetched so the next request hits the cache.
        if (fetchedThisCall.Count > 0)
        {
            try
            {
                await _priceQuoteRepository.UpsertManyAsync(fetchedThisCall, cancellationToken);
            }
            catch
            {
                // A cache-write failure shouldn't break the response — the user still
                // sees fresh prices, we just won't benefit from the cache next time.
            }
        }

        return result;
    }

    private static List<PositionEntity> AggregatePositions(IReadOnlyList<TradeEntity> trades)
    {
        return trades
            .GroupBy(t => t.Instrument)
            .Select(g => AggregateSingle(g.Key, g.OrderBy(t => t.ExecutedAt).ToList()))
            .ToList();
    }

    private static PositionEntity AggregateSingle(
        Valyze.Domain.Instruments.InstrumentRef instrument,
        IReadOnlyList<TradeEntity> trades)
    {
        var first = trades[0];
        var currency = first.Price.Currency;

        // Friendly display name — take the most recent trade that recorded one,
        // so renames or naming improvements propagate forward without losing data.
        var displayName = trades
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .OrderByDescending(t => t.ExecutedAt)
            .Select(t => t.Name)
            .FirstOrDefault();

        decimal qty = 0m;
        decimal cost = 0m;
        decimal realized = 0m;

        foreach (var t in trades)
        {
            if (t.Price.Currency != currency)
            {
                // Mixed-currency for one instrument is rare but possible (re-listings,
                // dual-listed shares). v1: skip the trade and warn — we'll add FX
                // normalization when a real case appears.
                continue;
            }

            var notional = t.Quantity * t.Price.Amount;
            if (t.Side == TradeSide.Buy)
            {
                qty += t.Quantity;
                cost += notional + t.Fees.Amount;
            }
            else
            {
                var avg = qty > 0 ? cost / qty : 0m;
                realized += t.Quantity * (t.Price.Amount - avg) - t.Fees.Amount;
                qty -= t.Quantity;
                cost -= t.Quantity * avg;
                if (qty <= 0)
                {
                    qty = Math.Max(qty, 0m);
                    cost = 0m;
                }
            }
        }

        var avgCost = qty > 0 ? cost / qty : 0m;

        return new PositionEntity
        {
            Instrument = instrument,
            Name = displayName,
            Quantity = qty,
            AvgCost = new MoneyValue(avgCost, currency),
            TotalCost = new MoneyValue(cost, currency),
            RealizedPnl = new MoneyValue(realized, currency),
            TradeCount = trades.Count,
            FirstTradeAt = trades.Min(t => t.ExecutedAt),
            LastTradeAt = trades.Max(t => t.ExecutedAt),
            Trades = trades,
        };
    }
}
