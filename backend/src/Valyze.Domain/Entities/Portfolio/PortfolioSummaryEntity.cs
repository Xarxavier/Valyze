using Valyze.Domain.Money;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Domain.Entities.Portfolio;

/// <summary>
/// Aggregate global view of an account's investment performance.
/// All amounts are expressed in <see cref="BaseCurrency"/>; foreign-currency
/// trades are surfaced via <see cref="ForeignTotalsInvested"/> and excluded
/// from the base totals until FX conversion is wired in.
/// <see cref="ValuationCoverage"/> is the fraction of base-currency cost basis
/// that has a current price quote — 1.0 means every position was priced.
/// </summary>
public sealed class PortfolioSummaryEntity
{
    public Currency BaseCurrency { get; set; }
    public DateTimeOffset AsOf { get; set; }

    public MoneyValue TotalInvested { get; set; }
    public MoneyValue TotalCurrentValue { get; set; }
    public MoneyValue TotalUnrealizedPnl { get; set; }
    public MoneyValue TotalRealizedPnl { get; set; }
    public MoneyValue TotalPnl { get; set; }

    public int OpenPositionCount { get; set; }
    public int TradeCount { get; set; }
    public decimal ValuationCoverage { get; set; }

    public IReadOnlyList<MoneyValue> ForeignTotalsInvested { get; set; } = [];
}
