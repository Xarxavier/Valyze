using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Valyze.Infraestructure.MarketData.Yahoo.Internal;

/// <summary>
/// ISIN → Yahoo-ready ticker candidates. Tries OpenFIGI's free /v3/mapping
/// first (best for stocks/ETFs across exchanges); falls back to Yahoo's
/// /v1/finance/search when OpenFIGI returns nothing usable — that path
/// covers open-end UCITS mutual funds (e.g. iShares Japan IE00BDRK7T12),
/// which OpenFIGI maps to Bloomberg-only exchanges Yahoo doesn't price.
/// One ISIN often maps to several listings; candidates are returned ordered
/// by EUR-likelihood so the price feed tries the most useful one first.
/// Cached in-process for 24h since these mappings are stable.
/// </summary>
internal sealed class OpenFigiIsinResolver : IIsinTickerResolver
{
    public const string HttpClientName = "openfigi";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// OpenFIGI exchange code → Yahoo Finance ticker suffix (or empty for US).
    /// Ordered roughly by Eurozone-first preference inside <see cref="ExchangeOrder"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ExchangeToYahooSuffix =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Eurozone (EUR-denominated listings — best for an EUR account)
            ["GR"] = ".DE", // Xetra (Deutsche Börse)
            ["GY"] = ".DE", // Xetra alias
            ["GF"] = ".F",  // Frankfurt
            ["IM"] = ".MI", // Borsa Italiana
            ["FP"] = ".PA", // Euronext Paris
            ["NA"] = ".AS", // Euronext Amsterdam
            ["BB"] = ".BR", // Euronext Brussels
            ["SM"] = ".MC", // Bolsa Madrid
            ["LS"] = ".LS", // Euronext Lisbon
            ["AV"] = ".VI", // Vienna
            ["FH"] = ".HE", // Helsinki
            ["DU"] = ".DU", // Düsseldorf
            // Non-Eurozone Europe
            ["LN"] = ".L",  // London
            ["SW"] = ".SW", // SIX Swiss
            ["VX"] = ".SW", // SIX Swiss alias
            ["SS"] = ".ST", // Stockholm
            ["SE"] = ".ST", // Stockholm alias
            ["DC"] = ".CO", // Copenhagen
            ["NO"] = ".OL", // Oslo
            // North America (no suffix)
            ["US"] = "",
            ["UN"] = "", // NYSE
            ["UQ"] = "", // NASDAQ
            ["UV"] = "", // NYSE Arca
            ["UA"] = "", // NYSE American
        };

    /// <summary>
    /// Order in which we present candidates: prefer EUR-quoted Eurozone, then
    /// non-Eurozone Europe, then US. The price feed iterates and stops at the
    /// first working ticker.
    /// </summary>
    private static readonly string[] ExchangeOrder =
    [
        "GR", "GY", "GF", "IM", "FP", "NA", "BB", "SM", "LS", "AV", "FH", "DU",
        "LN", "SW", "VX", "SS", "SE", "DC", "NO",
        "US", "UN", "UQ", "UV", "UA",
    ];

    /// <summary>
    /// Yahoo exchange-code preference for the search-fallback path. Lower index = tried first.
    /// Mirrors the EUR-first bias of <see cref="ExchangeOrder"/> but using the codes Yahoo
    /// actually returns (FRA, GER, MIL, …) instead of Bloomberg's.
    /// </summary>
    private static readonly string[] YahooExchangeOrder =
    [
        "GER", "FRA", "MIL", "PAR", "AMS", "BRU", "MCE", "LIS", "VIE", "HEL",
        "LSE", "EBS", "STO", "CPH", "OSL",
        "NMS", "NYQ", "NGM", "ASE", "PCX",
    ];

    private readonly HttpClient _http;
    private readonly HttpClient _yahoo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OpenFigiIsinResolver> _logger;

    public OpenFigiIsinResolver(
        IHttpClientFactory factory,
        IMemoryCache cache,
        ILogger<OpenFigiIsinResolver> logger)
    {
        _http = factory.CreateClient(HttpClientName);
        _yahoo = factory.CreateClient(YahooFinancePriceFeed.HttpClientName);
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> ResolveAsync(string isin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(isin)) return [];
        var key = $"isin-resolver:{isin.ToUpperInvariant()}";
        if (_cache.TryGetValue<IReadOnlyList<string>>(key, out var cached) && cached is not null)
            return cached;

        var fromOpenFigi = await ResolveViaOpenFigiAsync(isin, cancellationToken);
        if (fromOpenFigi.Count > 0)
            return CacheAndReturn(key, fromOpenFigi);

        // OpenFIGI returned nothing Yahoo can price — typical for UCITS mutual funds
        // (Bloomberg's "ID" exchange code, no public Yahoo suffix). Yahoo's search
        // endpoint accepts the ISIN directly and returns Morningstar-format symbols
        // (e.g. 0P0001AN9I.F) that the chart endpoint *does* price.
        var fromYahooSearch = await ResolveViaYahooSearchAsync(isin, cancellationToken);
        return CacheAndReturn(key, fromYahooSearch);
    }

    private async Task<IReadOnlyList<string>> ResolveViaOpenFigiAsync(string isin, CancellationToken ct)
    {
        try
        {
            var request = new[] { new MappingRequest("ID_ISIN", isin) };
            using var response = await _http.PostAsJsonAsync("v3/mapping", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenFIGI returned {Status} for {Isin}", response.StatusCode, isin);
                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<MappingResponse[]>(ct);
            var rows = payload?
                .FirstOrDefault()?
                .Data?
                .Where(d => !string.IsNullOrEmpty(d.Ticker))
                .ToList() ?? [];

            var candidates = rows
                .Select(r => new
                {
                    Row = r,
                    SuffixKnown = !string.IsNullOrEmpty(r.ExchCode)
                        && ExchangeToYahooSuffix.ContainsKey(r.ExchCode),
                    Order = !string.IsNullOrEmpty(r.ExchCode)
                        ? Array.IndexOf(ExchangeOrder, r.ExchCode.ToUpperInvariant())
                        : -1,
                })
                .Where(x => x.SuffixKnown)
                .OrderBy(x => x.Order < 0 ? int.MaxValue : x.Order)
                .Select(x => $"{x.Row.Ticker}{ExchangeToYahooSuffix[x.Row.ExchCode!]}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation(
                "OpenFIGI resolved {Isin} → {Count} candidate(s): {Candidates}",
                isin, candidates.Count, string.Join(", ", candidates));

            return candidates;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "OpenFIGI lookup failed for {Isin}", isin);
            return [];
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("OpenFIGI lookup timed out for {Isin}", isin);
            return [];
        }
    }

    private async Task<IReadOnlyList<string>> ResolveViaYahooSearchAsync(string isin, CancellationToken ct)
    {
        var url = $"v1/finance/search?q={Uri.EscapeDataString(isin)}&quotesCount=10";
        try
        {
            using var response = await _yahoo.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Yahoo search returned {Status} for {Isin}", response.StatusCode, isin);
                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<YahooSearchResponse>(ct);
            var quotes = payload?.Quotes?
                .Where(q => !string.IsNullOrEmpty(q.Symbol))
                .ToList() ?? [];

            var candidates = quotes
                .Select(q => new
                {
                    Symbol = q.Symbol!,
                    Order = !string.IsNullOrEmpty(q.Exchange)
                        ? Array.IndexOf(YahooExchangeOrder, q.Exchange.ToUpperInvariant())
                        : -1,
                })
                .OrderBy(x => x.Order < 0 ? int.MaxValue : x.Order)
                .Select(x => x.Symbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation(
                "Yahoo search resolved {Isin} → {Count} candidate(s): {Candidates}",
                isin, candidates.Count, string.Join(", ", candidates));

            return candidates;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Yahoo search failed for {Isin}", isin);
            return [];
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Yahoo search timed out for {Isin}", isin);
            return [];
        }
    }

    private IReadOnlyList<string> CacheAndReturn(string key, IReadOnlyList<string> value)
    {
        _cache.Set(key, value, CacheTtl);
        return value;
    }

    private sealed record MappingRequest(
        [property: JsonPropertyName("idType")] string IdType,
        [property: JsonPropertyName("idValue")] string IdValue);

    private sealed class MappingResponse
    {
        [JsonPropertyName("data")]
        public List<MappingHit>? Data { get; set; }
    }

    private sealed class MappingHit
    {
        [JsonPropertyName("ticker")]
        public string? Ticker { get; set; }

        [JsonPropertyName("exchCode")]
        public string? ExchCode { get; set; }

        [JsonPropertyName("securityType")]
        public string? SecurityType { get; set; }
    }

    private sealed class YahooSearchResponse
    {
        [JsonPropertyName("quotes")]
        public List<YahooQuoteHit>? Quotes { get; set; }
    }

    private sealed class YahooQuoteHit
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("exchange")]
        public string? Exchange { get; set; }

        [JsonPropertyName("quoteType")]
        public string? QuoteType { get; set; }
    }
}
