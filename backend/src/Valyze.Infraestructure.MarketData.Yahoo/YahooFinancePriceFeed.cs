using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Valyze.Domain.Application.MarketData;
using Valyze.Domain.Instruments;
using Valyze.Domain.Money;
using Valyze.Infraestructure.MarketData.Yahoo.Internal;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Infraestructure.MarketData.Yahoo;

/// <summary>
/// Equities/ETFs/ADRs spot price via Yahoo Finance's public <c>/v8/finance/chart</c> endpoint.
/// Resolves ISINs (e.g. US8740391003) to Yahoo tickers (TSM) via OpenFIGI when needed.
/// Returns prices in the security's native trading currency — FX conversion to the
/// account base currency is the caller's responsibility (and is gated until the FX
/// feed lands; mismatched-currency prices won't contribute to portfolio totals yet).
/// </summary>
public sealed class YahooFinancePriceFeed : IPriceFeed
{
    public const string ProviderKey = "yahoo";
    public const string HttpClientName = "yahoo";

    public static readonly Regex IsinPattern = new(@"^[A-Z]{2}[A-Z0-9]{9}[0-9]$", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly IIsinTickerResolver _resolver;
    private readonly ILogger<YahooFinancePriceFeed> _logger;

    public YahooFinancePriceFeed(
        IHttpClientFactory factory,
        IIsinTickerResolver resolver,
        ILogger<YahooFinancePriceFeed> logger)
    {
        _http = factory.CreateClient(HttpClientName);
        _resolver = resolver;
        _logger = logger;
    }

    public string Provider => ProviderKey;

    public async Task<IReadOnlyDictionary<string, MoneyValue>> GetCurrentPricesAsync(
        IReadOnlyCollection<string> symbols,
        Currency targetCurrency,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, MoneyValue>(StringComparer.OrdinalIgnoreCase);
        if (symbols.Count == 0) return result;

        foreach (var symbol in symbols)
        {
            // Crypto tickers are owned by the dedicated crypto feed. Yahoo will
            // happily answer for "BTC" with a tiny ~$36 fund that happens to use
            // that ticker — which would silently poison the cache. Refuse here.
            if (CryptoCatalog.IsCrypto(symbol)) continue;

            // Resolve symbol → ordered list of candidate Yahoo tickers.
            // ISINs go through OpenFIGI (returns multiple exchange listings);
            // anything else is treated as already a ticker.
            IReadOnlyList<string> candidates = IsinPattern.IsMatch(symbol)
                ? await _resolver.ResolveAsync(symbol, cancellationToken)
                : [symbol];

            if (candidates.Count == 0)
            {
                _logger.LogInformation("No Yahoo candidates for {Symbol}", symbol);
                continue;
            }

            // Try each candidate in order; first one that returns a price wins.
            // The resolver lists Eurozone exchanges first, so an EUR-quoted listing
            // is preferred when available — saves a downstream FX conversion.
            foreach (var candidate in candidates)
            {
                var price = await FetchOneAsync(candidate, cancellationToken);
                if (price is null) continue;

                result[symbol] = price.Value;
                _logger.LogInformation(
                    "Yahoo priced {Symbol} via {Ticker} = {Amount} {Currency}",
                    symbol, candidate, price.Value.Amount, price.Value.Currency);

                if (price.Value.Currency != targetCurrency)
                {
                    _logger.LogInformation(
                        "{Symbol} priced in {Currency}; will be FX-converted to {Target} downstream.",
                        symbol, price.Value.Currency, targetCurrency);
                }
                break;
            }
        }

        return result;
    }

    private async Task<MoneyValue?> FetchOneAsync(string ticker, CancellationToken ct)
    {
        var url = $"v8/finance/chart/{Uri.EscapeDataString(ticker)}?interval=1d&range=1d";
        try
        {
            var payload = await _http.GetFromJsonAsync<ChartResponse>(url, ct);
            var meta = payload?.Chart?.Result?.FirstOrDefault()?.Meta;
            if (meta is null) return null;
            if (meta.RegularMarketPrice is null) return null;
            if (string.IsNullOrEmpty(meta.Currency)) return null;
            return new MoneyValue(meta.RegularMarketPrice.Value, new Currency(meta.Currency));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Yahoo Finance request failed for {Ticker}", ticker);
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Yahoo Finance request timed out for {Ticker}", ticker);
            return null;
        }
    }

    // Minimal projection of Yahoo's chart payload — only the bits we actually read.
    private sealed class ChartResponse
    {
        [JsonPropertyName("chart")]
        public ChartBlock? Chart { get; set; }
    }

    private sealed class ChartBlock
    {
        [JsonPropertyName("result")]
        public List<ChartResult>? Result { get; set; }
    }

    private sealed class ChartResult
    {
        [JsonPropertyName("meta")]
        public ChartMeta? Meta { get; set; }
    }

    private sealed class ChartMeta
    {
        [JsonPropertyName("regularMarketPrice")]
        public decimal? RegularMarketPrice { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }
    }
}
