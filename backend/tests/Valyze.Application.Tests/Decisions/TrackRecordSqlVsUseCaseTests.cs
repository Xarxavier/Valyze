using Xunit;

namespace Valyze.Application.Tests.Decisions;

/// <summary>
/// Regression guard: ensures the SQL aggregation in InvestmentDecisionQueryService.GetTrackRecordAsync
/// produces the same per-source counts as row-by-row EvaluateDecisionUseCase computation.
///
/// Strategy:
///   1. Seed a known set of investment_decisions + price_quotes rows in Postgres.
///   2. Run EvaluateDecisionUseCase for each decision to compute expected statuses.
///   3. Run GetTrackRecordAsync to get SQL-aggregated counts.
///   4. Assert counts match exactly.
///
/// TODO: Wire up Testcontainers (Npgsql.Testcontainers or Testcontainers.PostgreSql)
///       and run the full migration on a fresh container for each test run.
///       Until then this test is skipped to keep the suite green without requiring
///       a live Postgres instance in CI.
/// </summary>
public sealed class TrackRecordSqlVsUseCaseTests
{
    [Fact(Skip = "Integration test — requires Postgres Testcontainers (deferred). " +
                 "See TODO: add Testcontainers.PostgreSql to backend integration test project " +
                 "and wire up DB seeding + migration before enabling.")]
    public async Task Sql_aggregation_matches_row_by_row_use_case_evaluation()
    {
        // TODO:
        // 1. Spin up a Postgres container via Testcontainers.PostgreSql
        // 2. Run `dotnet ef database update` (or apply migration SQL directly)
        // 3. Seed:
        //    - 3 decisions with ISIN="IE00B4L5Y983", action=BUY, PriceAtDecision=100 EUR, horizonDays=30, createdAt=60 days ago
        //    - price_quotes row for ISIN="IE00B4L5Y983", amount=110 EUR → expected ACHIEVED
        //    - 1 decision with ISIN="US0378331005", action=SELL, PriceAtDecision=200 USD, horizonDays=30, createdAt=60 days ago
        //    - price_quotes row for ISIN="US0378331005", amount=180 USD → return=-10% → ACHIEVED for SELL
        //    - 1 decision with action=HOLD, isin=null → NOT_APPLICABLE
        //    - 1 decision with action=REBALANCE, isin=null → MIXED
        // 4. Run EvaluateDecisionUseCase for each decision
        // 5. Run GetTrackRecordAsync
        // 6. Assert:
        //    Source=AiRecommendation: Total=3, Achieved=3 (the 3 BUY decisions)
        //    Source=UserOwnAnalysis: Total=2, Achieved=1 (SELL), Mixed=1 (REBALANCE)
        //    etc.
        await Task.CompletedTask; // placeholder
    }
}
