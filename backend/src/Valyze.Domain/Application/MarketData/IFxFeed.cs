using Valyze.Domain.Money;

namespace Valyze.Domain.Application.MarketData;

/// <summary>
/// Foreign-exchange rate provider. Returns the multiplier to convert one unit
/// of <paramref name="from"/> into <paramref name="to"/> — i.e., applying the
/// rate to a value in <paramref name="from"/> produces an equivalent value in
/// <paramref name="to"/>. Implementations may handle a finite currency set;
/// when a pair is unsupported the method returns <c>null</c>.
/// </summary>
public interface IFxFeed
{
    string Provider { get; }

    Task<decimal?> GetRateAsync(
        Currency from,
        Currency to,
        CancellationToken cancellationToken = default);
}
