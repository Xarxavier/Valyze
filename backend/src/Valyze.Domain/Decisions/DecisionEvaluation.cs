using Valyze.Domain.Enum;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Domain.Decisions;

/// <summary>
/// Computed evaluation result for a single decision. Never stored — always computed live.
/// </summary>
public sealed record DecisionEvaluation(
    DecisionStatus Status,
    decimal? ReturnPercent,
    int DaysElapsed,
    int Horizon,
    MoneyValue? PriceThen,
    MoneyValue? PriceNow,
    string? Message);
