using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Valyze.Domain.Application.MarketData;
using Valyze.Domain.Money;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Infraestructure.MarketData.CoinGecko;

/// <summary>
/// Free, key-less crypto spot prices via CoinGecko's <c>/simple/price</c> endpoint.
/// Symbols are matched against an in-process whitelist of common Trade Republic
/// crypto listings — unknown tickers are returned as missing rather than guessed.
/// </summary>
public sealed class CoinGeckoPriceFeed : IPriceFeed
{
    public const string ProviderKey = "coingecko";
    public const string HttpClientName = "coingecko";

    private static readonly IReadOnlyDictionary<string, string> TickerMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAVE"] = "aave",
            ["ADA"] = "cardano",
            ["ALGO"] = "algorand",
            ["APE"] = "apecoin",
            ["APT"] = "aptos",
            ["ARB"] = "arbitrum",
            ["ATOM"] = "cosmos",
            ["AVAX"] = "avalanche-2",
            ["AXS"] = "axie-infinity",
            ["BCH"] = "bitcoin-cash",
            ["BNB"] = "binancecoin",
            ["BTC"] = "bitcoin",
            ["CHZ"] = "chiliz",
            ["COMP"] = "compound-governance-token",
            ["DAI"] = "dai",
            ["DOGE"] = "dogecoin",
            ["DOT"] = "polkadot",
            ["ENJ"] = "enjincoin",
            ["ETH"] = "ethereum",
            ["FET"] = "fetch-ai",
            ["FIL"] = "filecoin",
            ["FTM"] = "fantom",
            ["GRT"] = "the-graph",
            ["HBAR"] = "hedera-hashgraph",
            ["ICP"] = "internet-computer",
            ["IMX"] = "immutable-x",
            ["INJ"] = "injective-protocol",
            ["LDO"] = "lido-dao",
            ["LINK"] = "chainlink",
            ["LTC"] = "litecoin",
            ["MANA"] = "decentraland",
            ["MATIC"] = "matic-network",
            ["MKR"] = "maker",
            ["NEAR"] = "near",
            ["OP"] = "optimism",
            ["RNDR"] = "render-token",
            ["SAND"] = "the-sandbox",
            ["SHIB"] = "shiba-inu",
            ["SNX"] = "havven",
            ["SOL"] = "solana",
            ["SUI"] = "sui",
            ["TON"] = "the-open-network",
            ["TRX"] = "tron",
            ["UNI"] = "uniswap",
            ["USDC"] = "usd-coin",
            ["USDT"] = "tether",
            ["XLM"] = "stellar",
            ["XRP"] = "ripple",
            ["XTZ"] = "tezos",
        };

    private readonly HttpClient _http;
    private readonly ILogger<CoinGeckoPriceFeed> _logger;

    public CoinGeckoPriceFeed(IHttpClientFactory factory, ILogger<CoinGeckoPriceFeed> logger)
    {
        _http = factory.CreateClient(HttpClientName);
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

        var resolved = symbols
            .Select(s => (Ticker: s, CoinId: TickerMap.TryGetValue(s, out var id) ? id : null))
            .Where(x => x.CoinId is not null)
            .ToList();
        if (resolved.Count == 0) return result;

        var ids = string.Join(",", resolved.Select(x => x.CoinId));
        var vs = targetCurrency.Code.ToLowerInvariant();
        var url = $"simple/price?ids={Uri.EscapeDataString(ids)}&vs_currencies={Uri.EscapeDataString(vs)}";

        try
        {
            var payload = await _http.GetFromJsonAsync<Dictionary<string, Dictionary<string, decimal>>>(
                url,
                cancellationToken);
            if (payload is null) return result;

            foreach (var (ticker, coinId) in resolved)
            {
                if (coinId is null) continue;
                if (!payload.TryGetValue(coinId, out var byCcy)) continue;
                if (!byCcy.TryGetValue(vs, out var amount)) continue;
                result[ticker] = new MoneyValue(amount, targetCurrency);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "CoinGecko request failed for {Symbols} → {Currency}",
                string.Join(",", symbols), vs);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("CoinGecko request timed out for {SymbolCount} symbols", symbols.Count);
        }

        return result;
    }
}
