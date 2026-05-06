namespace Valyze.Infraestructure.EntityFramework.Entities;

public sealed class InvestmentDecision
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>DecisionSource enum value persisted as smallint.</summary>
    public short Source { get; set; }

    /// <summary>DecisionAction enum value persisted as smallint.</summary>
    public short Action { get; set; }

    public string? Isin { get; set; }
    public string? Ticker { get; set; }

    public decimal? QuantityAmount { get; set; }

    /// <summary>ISO 4217 currency code. Only set when QuantityUnits = AmountBaseCcy.</summary>
    public string? QuantityCurrency { get; set; }

    /// <summary>QuantityUnits enum value persisted as smallint.</summary>
    public short QuantityUnits { get; set; }

    /// <summary>Price snapshot at decision time. NULL when price feed was unavailable (both columns together).</summary>
    public decimal? PriceAtDecisionAmount { get; set; }

    /// <summary>ISO 4217 currency code. NULL when PriceAtDecisionAmount is NULL.</summary>
    public string? PriceAtDecisionCurrency { get; set; }

    public string Rationale { get; set; } = null!;
    public int EvaluationHorizonDays { get; set; }

    /// <summary>Populated by SDD #3 (chat-persistence-DB). NULL in v1.</summary>
    public Guid? AiChatSessionId { get; set; }

    /// <summary>FK → trades(id). ON DELETE SET NULL — decisions outlive trades.</summary>
    public Guid? LinkedTradeId { get; set; }

    /// <summary>Only meaningful when Source = Other (5).</summary>
    public string? SourceOtherNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
