using Valyze.Domain.Instruments;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Domain.Entities.Portfolio;

/// <summary>
/// A holding for one instrument, derived from the trade history.
/// Quantity is qty bought minus qty sold; AvgCost is the weighted-average
/// purchase price across all open lots; RealizedPnl accumulates from sells.
/// CurrentPrice/Value/UnrealizedPnl are populated when a price feed has a
/// quote for <see cref="Instrument"/>; otherwise they are null and
/// <see cref="Valued"/> is false.
/// </summary>
public sealed class PositionEntity
{
    public InstrumentRef Instrument { get; set; }

    /// <summary>
    /// Human-friendly name carried over from the most recent trade that
    /// recorded one. Null when no trade in the aggregate provided a name.
    /// </summary>
    public string? Name { get; set; }
    public decimal Quantity { get; set; }
    public MoneyValue AvgCost { get; set; }
    public MoneyValue TotalCost { get; set; }
    public MoneyValue RealizedPnl { get; set; }

    public bool Valued { get; set; }
    public MoneyValue? CurrentPrice { get; set; }
    public MoneyValue? CurrentValue { get; set; }
    public MoneyValue? UnrealizedPnl { get; set; }
    public decimal? UnrealizedPnlPercent { get; set; }

    /// <summary>
    /// Best-effort estimate of the broker commission you'd pay to close (sell)
    /// this position right now. Derived from the most recent trade's fee for
    /// the same instrument — for Trade Republic this is always 1 EUR per fill.
    /// In the account base currency.
    /// </summary>
    public MoneyValue? EstimatedSellCommission { get; set; }

    /// <summary>
    /// Net of the would-be sell commission: CurrentValue − EstimatedSellCommission.
    /// "What you'd actually pocket if you closed today."
    /// </summary>
    public MoneyValue? NetCurrentValue { get; set; }

    /// <summary>
    /// Unrealized P&L net of buy fees (already in cost basis) and the estimated
    /// sell commission: NetCurrentValue − TotalCost.
    /// </summary>
    public MoneyValue? NetUnrealizedPnl { get; set; }

    public int TradeCount { get; set; }
    public DateTimeOffset? FirstTradeAt { get; set; }
    public DateTimeOffset? LastTradeAt { get; set; }

    /// <summary>
    /// All trades that contributed to this position, sorted by execution date
    /// ascending. Surfaced in the API response so the UI can show a per-position
    /// trade history without an extra round-trip.
    /// </summary>
    public IReadOnlyList<TradeEntity> Trades { get; set; } = [];
}
