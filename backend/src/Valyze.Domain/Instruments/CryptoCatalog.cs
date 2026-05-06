namespace Valyze.Domain.Instruments;

/// <summary>
/// Canonical list of crypto tickers Valyze knows about. Lives in Domain so any
/// price feed can consult it: crypto adapters (CoinGecko) use it as their input
/// space, and equity adapters (Yahoo) use it as a NEGATIVE list — Yahoo answers
/// for a stock literally tickered "BTC" with a tiny ~$36 fund, which would
/// silently overwrite the real Bitcoin price if it weren't excluded here.
/// </summary>
public static class CryptoCatalog
{
    public static readonly IReadOnlySet<string> KnownTickers =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AAVE", "ADA", "ALGO", "APE", "APT", "ARB", "ATOM", "AVAX", "AXS",
            "BCH", "BNB", "BTC", "CHZ", "COMP", "DAI", "DOGE", "DOT", "ENJ",
            "ETH", "FET", "FIL", "FTM", "GRT", "HBAR", "ICP", "IMX", "INJ",
            "LDO", "LINK", "LTC", "MANA", "MATIC", "MKR", "NEAR", "OP", "RNDR",
            "SAND", "SHIB", "SNX", "SOL", "SUI", "TON", "TRX", "UNI", "USDC",
            "USDT", "XLM", "XRP", "XTZ",
        };

    public static bool IsCrypto(string ticker) => KnownTickers.Contains(ticker);
}
