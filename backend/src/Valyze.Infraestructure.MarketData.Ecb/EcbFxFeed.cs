using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Valyze.Domain.Application.MarketData;
using Valyze.Domain.Money;

namespace Valyze.Infraestructure.MarketData.Ecb;

/// <summary>
/// EUR-quoted reference rates from the European Central Bank's daily XML feed.
/// Free, no auth, no rate limit. Rates published once per day on TARGET2
/// business days. We fetch the full table once and cache for 6 hours; that's
/// more frequent than ECB updates while keeping outbound HTTP at one call per
/// 4× per day even under heavy app usage.
/// </summary>
public sealed class EcbFxFeed : IFxFeed
{
    public const string ProviderKey = "ecb";
    public const string HttpClientName = "ecb";
    private const string CacheKey = "ecb:eur-rates";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    private static readonly XNamespace GesmesNs = "http://www.gesmes.org/xml/2002-08-01";
    private static readonly XNamespace EcbNs = "http://www.ecb.int/vocabulary/2002-08-01/eurofxref";

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EcbFxFeed> _logger;

    public EcbFxFeed(IHttpClientFactory factory, IMemoryCache cache, ILogger<EcbFxFeed> logger)
    {
        _http = factory.CreateClient(HttpClientName);
        _cache = cache;
        _logger = logger;
    }

    public string Provider => ProviderKey;

    public async Task<decimal?> GetRateAsync(
        Currency from,
        Currency to,
        CancellationToken cancellationToken = default)
    {
        if (from == to) return 1m;

        var rates = await GetEurRatesAsync(cancellationToken);
        if (rates is null) return null;

        // ECB publishes rates AS "1 EUR = X foreign". Convert into a generic
        // multiplier from any currency to any other via EUR as pivot.
        // amount_in_to = amount_in_from * (rateTo / rateFrom)
        var rateFrom = from.Code == "EUR" ? 1m : (rates.TryGetValue(from.Code, out var rf) ? rf : (decimal?)null);
        var rateTo = to.Code == "EUR" ? 1m : (rates.TryGetValue(to.Code, out var rt) ? rt : (decimal?)null);
        if (rateFrom is null || rateTo is null) return null;
        if (rateFrom == 0m) return null;

        return rateTo.Value / rateFrom.Value;
    }

    private async Task<IReadOnlyDictionary<string, decimal>?> GetEurRatesAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue<IReadOnlyDictionary<string, decimal>>(CacheKey, out var hit) && hit is not null)
            return hit;

        try
        {
            await using var stream = await _http.GetStreamAsync("stats/eurofxref/eurofxref-daily.xml", ct);
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);

            var inner = doc
                .Descendants(EcbNs + "Cube")
                .FirstOrDefault(c => c.Attribute("time") is not null)
                ?.Elements(EcbNs + "Cube")
                .ToList();
            if (inner is null || inner.Count == 0)
            {
                _logger.LogWarning("ECB feed returned no rate rows");
                return null;
            }

            var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in inner)
            {
                var ccy = node.Attribute("currency")?.Value;
                var rateStr = node.Attribute("rate")?.Value;
                if (string.IsNullOrEmpty(ccy) || string.IsNullOrEmpty(rateStr)) continue;
                if (decimal.TryParse(rateStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate))
                    dict[ccy] = rate;
            }

            _cache.Set(CacheKey, (IReadOnlyDictionary<string, decimal>)dict, CacheTtl);
            return dict;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "ECB FX request failed");
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("ECB FX request timed out");
            return null;
        }
    }
}
