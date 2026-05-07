namespace Valyze.Application.Decisions;

/// <summary>
/// Configuration for investment decision evaluation.
/// Bound to appsettings section "Decisions:Evaluation".
/// </summary>
public sealed class DecisionEvaluationOptions
{
    /// <summary>
    /// Minimum return percentage to classify a decision as ACHIEVED (default: 5%).
    /// For BUY/HOLD: return must be >= threshold to be ACHIEVED; return <= -threshold = UNDERPERFORMING.
    /// For SELL: return must be <= -threshold (we wanted price down).
    /// </summary>
    public decimal AchievementThreshold { get; set; } = 0.05m;
}
