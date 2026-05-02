using Valyze.Domain.Money;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Domain.Application.MarketData;

/// <summary>
/// Resolves current prices for a set of instrument symbols. Implementations
/// may handle different asset classes (crypto, equities, ETFs) — when a
/// symbol is outside the implementation's coverage the entry is omitted from
/// the returned dictionary rather than thrown. Symbols are matched
/// case-insensitively.
/// </summary>
public interface IPriceFeed
{
    string Provider { get; }

    Task<IReadOnlyDictionary<string, MoneyValue>> GetCurrentPricesAsync(
        IReadOnlyCollection<string> symbols,
        Currency targetCurrency,
        CancellationToken cancellationToken = default);
}
