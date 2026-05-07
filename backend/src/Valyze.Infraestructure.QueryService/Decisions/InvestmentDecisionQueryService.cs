using Dapper;
using Microsoft.Extensions.Configuration;
using Valyze.Domain.Application.Decisions;
using Valyze.Domain.Decisions;
using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Enum;
using Valyze.Domain.Exceptions;
using Valyze.Domain.Money;
using Valyze.Domain.QueryService;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Infraestructure.QueryService.Decisions;

public class InvestmentDecisionQueryService : BaseQueryService, IInvestmentDecisionQueryService
{
    public InvestmentDecisionQueryService(IConfiguration configuration) : base(configuration) { }

    // ─── Dapper row projection ────────────────────────────────────────────────

    private sealed record DecisionRow(
        Guid Id,
        Guid AccountId,
        short Source,
        short Action,
        string? Isin,
        string? Ticker,
        decimal? QuantityAmount,
        string? QuantityCurrency,
        short QuantityUnits,
        decimal? PriceAtDecisionAmount,
        string? PriceAtDecisionCurrency,
        string Rationale,
        int EvaluationHorizonDays,
        Guid? AiChatSessionId,
        Guid? LinkedTradeId,
        string? SourceOtherNote,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private sealed record TrackRecordRow(
        short Source,
        Guid AccountId,
        int Total,
        int Achieved,
        int Underperforming,
        int Pending,
        int NotApplicable,
        int Mixed,
        decimal? AvgReturnPercent);

    // ─── ListByAccountAsync ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<InvestmentDecisionEntity>> ListByAccountAsync(
        Guid accountId,
        ListDecisionsQuery query,
        CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();

        const string baseSql = @"
            SELECT
                id                          AS Id,
                account_id                  AS AccountId,
                source                      AS Source,
                action                      AS Action,
                isin                        AS Isin,
                ticker                      AS Ticker,
                quantity_amount             AS QuantityAmount,
                quantity_currency           AS QuantityCurrency,
                quantity_units              AS QuantityUnits,
                price_at_decision_amount    AS PriceAtDecisionAmount,
                price_at_decision_currency  AS PriceAtDecisionCurrency,
                rationale                   AS Rationale,
                evaluation_horizon_days     AS EvaluationHorizonDays,
                ai_chat_session_id          AS AiChatSessionId,
                linked_trade_id             AS LinkedTradeId,
                source_other_note           AS SourceOtherNote,
                created_at                  AS CreatedAt,
                updated_at                  AS UpdatedAt
            FROM investment_decisions
            WHERE account_id = @AccountId
              AND (@Since IS NULL OR created_at >= @Since)
              AND (@Source IS NULL OR source = @Source)
              AND (@Action IS NULL OR action = @Action)
              AND (@Isin IS NULL OR isin = @Isin)
            ORDER BY created_at DESC
            LIMIT CASE WHEN @Limit IS NULL THEN 100 ELSE @Limit END;";

        var rows = (await connection.QueryAsync<DecisionRow>(baseSql, new
        {
            AccountId = accountId,
            Since = query.Since.HasValue ? (DateTime?)query.Since.Value.UtcDateTime : null,
            Source = query.Source.HasValue ? (short?)((short)query.Source.Value) : null,
            Action = query.Action.HasValue ? (short?)((short)query.Action.Value) : null,
            Isin = query.Isin,
            Limit = query.Limit,
        })).ToList();

        return rows
            .Select(MapDecisionRow)
            .Select(e => AccountGuard.EnforceSingle(e, accountId, d => d.AccountId))
            .ToList();
    }

    // ─── GetTrackRecordAsync ──────────────────────────────────────────────────

    /// <summary>
    /// Aggregates decision outcomes per source in a single SQL roundtrip.
    ///
    /// Status classification SQL mirrors EvaluateDecisionUseCase logic:
    ///   NOT_APPLICABLE: PriceAtDecision is NULL, OR (HOLD AND isin IS NULL), OR no current quote
    ///   PENDING_HORIZON: days_elapsed &lt; evaluation_horizon_days AND none of the above
    ///   MIXED: REBALANCE (any leg) — v1 approximation
    ///   SELL achieved: current_return_pct &lt;= -threshold*100
    ///   BUY/HOLD achieved: current_return_pct > -threshold*100
    ///
    /// WARNING: The SQL approximation uses the latest available price from price_quotes.
    /// If no current quote exists, it falls back to NOT_APPLICABLE (same as use case).
    /// This matches the use case behavior but CANNOT fetch fresh quotes — it uses cached data.
    /// The regression test (7.2) must assert that SQL and use case agree for known seeded data.
    /// </summary>
    public async Task<IReadOnlyList<DecisionTrackRecordRow>> GetTrackRecordAsync(
        Guid accountId,
        DecisionSource? sourceFilter,
        decimal achievementThreshold,
        CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();

        // achievementThreshold is e.g. 0.05m; SQL works in percentage points (5.0)
        var thresholdPct = achievementThreshold * 100m;

        const string sql = @"
            WITH latest_quote AS (
                -- For each isin, pick the single most recent price quote.
                SELECT DISTINCT ON (symbol)
                    symbol,
                    amount    AS current_amount,
                    currency  AS current_currency,
                    fetched_at
                FROM price_quotes
                ORDER BY symbol, fetched_at DESC
            ),
            enriched AS (
                SELECT
                    d.id,
                    d.account_id,
                    d.source,
                    d.action,
                    d.isin,
                    d.price_at_decision_amount,
                    d.price_at_decision_currency,
                    d.evaluation_horizon_days,
                    d.created_at,
                    EXTRACT(DAY FROM (NOW() AT TIME ZONE 'UTC') - d.created_at)::int  AS days_elapsed,
                    lq.current_amount,
                    lq.current_currency,
                    CASE
                        WHEN d.price_at_decision_amount IS NOT NULL
                             AND lq.current_amount IS NOT NULL
                             AND d.price_at_decision_currency = lq.current_currency
                        THEN ROUND(
                            (lq.current_amount - d.price_at_decision_amount)
                            / d.price_at_decision_amount * 100.0,
                            2
                        )
                        ELSE NULL
                    END AS return_pct
                FROM investment_decisions d
                LEFT JOIN latest_quote lq
                       ON lq.symbol = d.isin
                WHERE d.account_id = @AccountId
                  AND (@SourceFilter IS NULL OR d.source = @SourceFilter)
            ),
            classified AS (
                SELECT
                    source,
                    account_id,
                    CASE
                        -- HOLD without isin → NOT_APPLICABLE
                        WHEN action = 3 AND isin IS NULL THEN 'NOT_APPLICABLE'
                        -- NULL price at decision time → NOT_APPLICABLE
                        WHEN price_at_decision_amount IS NULL THEN 'NOT_APPLICABLE'
                        -- No current quote → NOT_APPLICABLE
                        WHEN current_amount IS NULL THEN 'NOT_APPLICABLE'
                        -- Currency mismatch → NOT_APPLICABLE (cannot compute return)
                        WHEN return_pct IS NULL THEN 'NOT_APPLICABLE'
                        -- Still within horizon → PENDING
                        WHEN days_elapsed < evaluation_horizon_days THEN 'PENDING'
                        -- REBALANCE → MIXED (v1 single-leg approximation)
                        WHEN action = 4 THEN 'MIXED'
                        -- SELL: favorable = price dropped beyond threshold
                        WHEN action = 2 AND return_pct <= -@ThresholdPct THEN 'ACHIEVED'
                        WHEN action = 2 THEN 'UNDERPERFORMING'
                        -- BUY / HOLD with instrument: unfavorable = dropped beyond threshold
                        WHEN return_pct <= -@ThresholdPct THEN 'UNDERPERFORMING'
                        ELSE 'ACHIEVED'
                    END AS status,
                    return_pct
                FROM enriched
            )
            SELECT
                source                                              AS Source,
                account_id                                         AS AccountId,
                COUNT(*)::int                                      AS Total,
                COUNT(*) FILTER (WHERE status = 'ACHIEVED')::int   AS Achieved,
                COUNT(*) FILTER (WHERE status = 'UNDERPERFORMING')::int AS Underperforming,
                COUNT(*) FILTER (WHERE status = 'PENDING')::int    AS Pending,
                COUNT(*) FILTER (WHERE status = 'NOT_APPLICABLE')::int AS NotApplicable,
                COUNT(*) FILTER (WHERE status = 'MIXED')::int      AS Mixed,
                AVG(return_pct) FILTER (WHERE status IN ('ACHIEVED', 'UNDERPERFORMING')) AS AvgReturnPercent
            FROM classified
            GROUP BY source, account_id
            ORDER BY source;";

        var rows = (await connection.QueryAsync<TrackRecordRow>(sql, new
        {
            AccountId = accountId,
            SourceFilter = sourceFilter.HasValue ? (short?)((short)sourceFilter.Value) : null,
            ThresholdPct = thresholdPct,
        })).ToList();

        // Post-validate: every row must belong to the requested account
        var validated = AccountGuard.EnforceMany(rows, accountId, r => r.AccountId).ToList();

        return validated.Select(r => new DecisionTrackRecordRow(
            Source: (DecisionSource)r.Source,
            Total: r.Total,
            Achieved: r.Achieved,
            Underperforming: r.Underperforming,
            Pending: r.Pending,
            NotApplicable: r.NotApplicable,
            Mixed: r.Mixed,
            AvgReturnPercent: r.AvgReturnPercent.HasValue
                ? Math.Round(r.AvgReturnPercent.Value, 2)
                : null
        )).ToList();
    }

    // ─── Row mapper ───────────────────────────────────────────────────────────

    private static InvestmentDecisionEntity MapDecisionRow(DecisionRow r) => new()
    {
        Id = r.Id,
        AccountId = r.AccountId,
        Source = (DecisionSource)r.Source,
        Action = (DecisionAction)r.Action,
        Isin = r.Isin,
        Ticker = r.Ticker,
        QuantityAmount = r.QuantityAmount,
        QuantityCurrency = r.QuantityCurrency is not null
            ? new Currency(r.QuantityCurrency)
            : null,
        QuantityUnits = (QuantityUnits)r.QuantityUnits,
        PriceAtDecision = r.PriceAtDecisionAmount.HasValue && r.PriceAtDecisionCurrency is not null
            ? new MoneyValue(r.PriceAtDecisionAmount.Value, new Currency(r.PriceAtDecisionCurrency))
            : null,
        Rationale = r.Rationale,
        EvaluationHorizonDays = r.EvaluationHorizonDays,
        AiChatSessionId = r.AiChatSessionId,
        LinkedTradeId = r.LinkedTradeId,
        SourceOtherNote = r.SourceOtherNote,
        CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc)),
        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc)),
    };
}
