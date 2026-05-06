using Valyze.Domain.Enum;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Domain.Entities.Decisions;

/// <summary>
/// Represents a user's investment intent at a point in time.
/// This is the source-of-truth record — the evaluation/status is computed live, never stored.
/// </summary>
public sealed class InvestmentDecisionEntity
{
    public Guid Id { get; set; }

    /// <summary>Multi-tenancy root. Every query MUST filter by this.</summary>
    public Guid AccountId { get; set; }

    public DecisionSource Source { get; set; }
    public DecisionAction Action { get; set; }

    /// <summary>Primary instrument key. Null for REBALANCE decisions without a specific instrument.</summary>
    public string? Isin { get; set; }

    /// <summary>Optional secondary lookup key.</summary>
    public string? Ticker { get; set; }

    public decimal? QuantityAmount { get; set; }

    /// <summary>Only set when QuantityUnits = AmountBaseCcy.</summary>
    public Valyze.Domain.Money.Currency? QuantityCurrency { get; set; }

    public QuantityUnits QuantityUnits { get; set; }

    /// <summary>
    /// Price snapshot captured at decision time. Both Amount and Currency are
    /// stored together in this Money VO — null when price feed was unavailable.
    /// Domain invariant: both null or both set — never one without the other.
    /// </summary>
    public MoneyValue? PriceAtDecision { get; set; }

    /// <summary>Free-text rationale. Required — enforced at use case boundary.</summary>
    public string Rationale { get; set; } = null!;

    /// <summary>Horizon in days after which the decision is evaluated. Defaults applied per action by use case.</summary>
    public int EvaluationHorizonDays { get; set; }

    /// <summary>Populated by SDD #3 (chat-persistence-DB). NULL in v1.</summary>
    public Guid? AiChatSessionId { get; set; }

    /// <summary>Set when the user manually links this decision to a recorded trade.</summary>
    public Guid? LinkedTradeId { get; set; }

    /// <summary>Only meaningful when Source = Other.</summary>
    public string? SourceOtherNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
