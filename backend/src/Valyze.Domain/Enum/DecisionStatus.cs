namespace Valyze.Domain.Enum;

/// <summary>
/// Computed at evaluation time — NEVER persisted to the database.
/// </summary>
public enum DecisionStatus : short
{
    PendingHorizon = 1,
    Achieved = 2,
    Underperforming = 3,
    Mixed = 4,
    NotApplicable = 5,
}
