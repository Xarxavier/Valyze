using Valyze.Domain.Enum;
using Valyze.Domain.Instruments;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Domain.Entities.Portfolio;

public sealed class TradeEntity
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public InstrumentRef Instrument { get; set; }
    public TradeSide Side { get; set; }
    public decimal Quantity { get; set; }
    public MoneyValue Price { get; set; }
    public MoneyValue Fees { get; set; }
    public DateTimeOffset ExecutedAt { get; set; }

    /// <summary>
    /// Source broker key (e.g. "trade-republic"). Required.
    /// </summary>
    public string BrokerKey { get; set; } = null!;

    /// <summary>
    /// Broker-issued unique identifier for this fill (e.g. TR's "EJECUCIÓN" id).
    /// Optional — manual entries may have none. When present, the
    /// (AccountId, BrokerKey, BrokerReference) tuple is unique to prevent
    /// re-importing the same trade.
    /// </summary>
    public string? BrokerReference { get; set; }

    /// <summary>
    /// Human-friendly asset name as printed on the broker's confirmation
    /// (e.g. "Bitcoin", "TSMC (ADR)"). Denormalized onto every trade so the
    /// position view doesn't need a separate instruments lookup. Optional —
    /// older trades or manual entries may have no name; the frontend falls
    /// back to <see cref="Instrument"/> when null.
    /// </summary>
    public string? Name { get; set; }
}
