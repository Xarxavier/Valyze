using Valyze.Domain.Enum;

namespace Valyze.Domain.Decisions;

/// <summary>
/// Per-source aggregation of decision outcomes for an account.
/// </summary>
public sealed record DecisionTrackRecord(IReadOnlyList<DecisionTrackRecordRow> BySource);

/// <summary>
/// Aggregated hit-rate stats for a single DecisionSource.
/// </summary>
public sealed record DecisionTrackRecordRow(
    DecisionSource Source,
    int Total,
    int Achieved,
    int Underperforming,
    int Pending,
    int NotApplicable,
    int Mixed,
    decimal? AvgReturnPercent);
