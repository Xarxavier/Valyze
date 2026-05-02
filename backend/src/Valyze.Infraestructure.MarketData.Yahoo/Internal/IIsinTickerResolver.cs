namespace Valyze.Infraestructure.MarketData.Yahoo.Internal;

/// <summary>
/// Resolves an ISIN (e.g. US8740391003 or IE00BDRK7T12) to one or more
/// candidate Yahoo-ready tickers. European UCITS ETFs typically have multiple
/// listings (Xetra, London, Milan…) — the resolver returns them ordered by
/// best EUR-price likelihood so the price feed tries the most useful one first.
/// </summary>
public interface IIsinTickerResolver
{
    Task<IReadOnlyList<string>> ResolveAsync(string isin, CancellationToken cancellationToken = default);
}
