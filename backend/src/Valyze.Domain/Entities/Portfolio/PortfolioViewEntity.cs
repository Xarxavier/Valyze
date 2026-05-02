using Valyze.Domain.Money;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Domain.Entities.Portfolio;

public sealed class PortfolioViewEntity
{
    public Guid AccountId { get; set; }
    public Currency BaseCurrency { get; set; }
    public int PositionCount { get; set; }
    public int TradeCount { get; set; }

    /// <summary>
    /// Net cash deployed in the account's base currency.
    /// Sum of (qty × price + fees) for buys, minus (qty × price − fees) for sells,
    /// limited to trades whose price currency equals the account's base currency.
    /// Trades in foreign currencies are surfaced separately via <see cref="ForeignTotals"/>.
    /// </summary>
    public MoneyValue TotalInvested { get; set; }

    /// <summary>
    /// Optional per-currency totals for trades NOT in the base currency.
    /// Empty when every trade is denominated in the base currency.
    /// </summary>
    public IReadOnlyList<MoneyValue> ForeignTotals { get; set; } = [];
}
