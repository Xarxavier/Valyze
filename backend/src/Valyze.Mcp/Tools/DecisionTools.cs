using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Valyze.Mcp.Backend;

namespace Valyze.Mcp.Tools;

/// <summary>
/// MCP tools for the investment decision tracker. These give the AI:
///   - Recording decisions as they happen (record_decision)
///   - Browsing past decisions (list_decisions)
///   - Evaluating how a decision played out (evaluate_decision)
///   - Viewing the overall hit-rate per source (get_decision_track_record)
///   - Linking a decision to the trade that executed it (link_decision_to_trade)
///
/// GUARDRAIL: Before calling record_decision the model MUST confirm with the
/// user which source applies. Source is never inferred — it is always explicit
/// user confirmation. See the Description on record_decision for details.
/// </summary>
[McpServerToolType]
public static class DecisionTools
{
    [McpServerTool(Name = "record_decision")]
    [Description(
        "Records a new investment decision for the user. " +
        "IMPORTANT: before calling this tool, you MUST ask the user which `source` applies. " +
        "Never infer it from context — always ask explicitly. " +
        "The 5 valid sources are: " +
        "AI_RECOMMENDATION (the recommendation came directly from this chat session), " +
        "USER_OWN_ANALYSIS (the user's own idea, independent of this chat), " +
        "EXTERNAL_NEWS (triggered by a news article, podcast, or research report the user read), " +
        "THIRD_PARTY_TIP (tip from a broker, friend, or paid research service), " +
        "OTHER (anything else — requires a short note in sourceOtherNote). " +
        "Supply isin for instrument-specific decisions (BUY, SELL, HOLD). " +
        "For REBALANCE decisions that aren't tied to a specific instrument, omit isin. " +
        "horizonDays defaults: BUY=180, SELL=30, HOLD=90, REBALANCE=90. " +
        "quantityUnits: SHARES (default), AMOUNT_BASE_CCY (euro/dollar amount), " +
        "PERCENT_PORTFOLIO (percentage of total portfolio).")]
    public static async Task<string> RecordDecisionAsync(
        ValyzeApiClient client,
        [Description("Decision source — must be confirmed with the user. " +
                     "Valid values: AI_RECOMMENDATION, USER_OWN_ANALYSIS, EXTERNAL_NEWS, " +
                     "THIRD_PARTY_TIP, OTHER.")] string source,
        [Description("Action taken: BUY, SELL, HOLD, or REBALANCE.")] string action,
        [Description("ISIN of the instrument (e.g. US0378331005). Omit for REBALANCE decisions without a specific instrument.")] string? isin,
        [Description("Ticker symbol (e.g. AAPL). Optional secondary key alongside isin.")] string? ticker,
        [Description("Quantity of shares/units/amount involved. Omit if not applicable.")] decimal? quantity,
        [Description("How quantity is measured: SHARES (default), AMOUNT_BASE_CCY, or PERCENT_PORTFOLIO.")] string? units,
        [Description("Free-text rationale — why the user is making this decision. Required.")] string rationale,
        [Description("Evaluation horizon in days. Defaults are applied per action when omitted.")] int? horizonDays,
        [Description("Required when source=OTHER: brief description of the source.")] string? sourceOtherNote,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            source,
            action,
            isin,
            ticker,
            quantityAmount = quantity,
            quantityUnits = units ?? "SHARES",
            rationale,
            horizonDays,
            sourceOtherNote,
        }, JsonOptions);
        return await client.PostJsonAsync("/api/decisions/", body, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "list_decisions")]
    [Description(
        "Lists investment decisions for the authenticated user. Results are ordered by " +
        "createdAt descending (most recent first). Use `source` to filter by origin " +
        "(e.g. AI_RECOMMENDATION to review only decisions that came from this chat). " +
        "Use `isin` to filter to one instrument. Use `since` (ISO 8601) to limit to " +
        "recent decisions. `limit` defaults to 25, max 100.")]
    public static async Task<string> ListDecisionsAsync(
        ValyzeApiClient client,
        [Description("Max decisions to return. Default 25.")] int? limit,
        [Description("ISO 8601 lower bound on createdAt. Omit for no lower bound.")] string? since,
        [Description("Filter by source: AI_RECOMMENDATION, USER_OWN_ANALYSIS, EXTERNAL_NEWS, " +
                     "THIRD_PARTY_TIP, or OTHER.")] string? source,
        [Description("Filter by action: BUY, SELL, HOLD, or REBALANCE.")] string? action,
        [Description("Filter by ISIN.")] string? isin,
        CancellationToken cancellationToken)
    {
        var query = "/api/decisions/";
        var hasParam = false;

        void Append(string key, string value)
        {
            query += (hasParam ? "&" : "?") + key + "=" + Uri.EscapeDataString(value);
            hasParam = true;
        }

        if (limit.HasValue) Append("limit", limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(since)) Append("since", since);
        if (!string.IsNullOrEmpty(source)) Append("source", source);
        if (!string.IsNullOrEmpty(action)) Append("action", action);
        if (!string.IsNullOrEmpty(isin)) Append("isin", isin);

        return await client.GetJsonAsync(query, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "evaluate_decision")]
    [Description(
        "Evaluates how a specific decision played out against its horizon and the current " +
        "market price. Returns: status (PENDING_HORIZON, ACHIEVED, UNDERPERFORMING, MIXED, " +
        "NOT_APPLICABLE), returnPercent (null when not yet evaluable), daysElapsed, horizon, " +
        "priceThen (price snapshot captured when the decision was recorded), and priceNow " +
        "(latest cached quote). Use this when the user asks 'how did that call go?' or " +
        "wants to see if a past decision was right.")]
    public static async Task<string> EvaluateDecisionAsync(
        ValyzeApiClient client,
        [Description("Decision id (UUID) from record_decision or list_decisions.")] string decisionId,
        CancellationToken cancellationToken)
    {
        return await client.GetJsonAsync(
            $"/api/decisions/{Uri.EscapeDataString(decisionId)}/evaluate",
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "get_decision_track_record")]
    [Description(
        "Returns aggregated hit-rate statistics for the user's decisions, grouped by source. " +
        "Each row contains: source, total, achieved, underperforming, pending, notApplicable, " +
        "mixed, avgReturnPercent. Use this for periodic reviews ('¿qué tan buenas son mis " +
        "decisiones?') or to check if AI recommendations have been more accurate than tips " +
        "from third parties. Filter by `source` to narrow to one origin.")]
    public static async Task<string> GetDecisionTrackRecordAsync(
        ValyzeApiClient client,
        [Description("Optional source filter: AI_RECOMMENDATION, USER_OWN_ANALYSIS, EXTERNAL_NEWS, " +
                     "THIRD_PARTY_TIP, or OTHER. Omit to see all sources.")] string? source,
        CancellationToken cancellationToken)
    {
        var query = "/api/decisions/track-record";
        if (!string.IsNullOrEmpty(source))
            query += "?source=" + Uri.EscapeDataString(source);

        return await client.GetJsonAsync(query, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "link_decision_to_trade")]
    [Description(
        "Links a recorded investment decision to the trade that executed it, so the user " +
        "can see 'I decided to buy AAPL on Jan 3 and executed it on Jan 5 at price X'. " +
        "Pass the decision UUID and the trade UUID to link. To CLEAR an existing link " +
        "(decision existed but the trade was deleted or misidentified), pass an empty string " +
        "or the string \"null\" as tradeId — both are treated as clear-link. " +
        "After a PDF import, list unlinked decisions for the imported instruments via " +
        "list_decisions and ask the user to confirm the matching trade before calling this.")]
    public static async Task<string> LinkDecisionToTradeAsync(
        ValyzeApiClient client,
        [Description("Decision id (UUID) to link.")] string decisionId,
        [Description("Trade id (UUID) to link to, or empty string / \"null\" to clear the link.")] string tradeId,
        CancellationToken cancellationToken)
    {
        // Normalise clear-link signals to JSON null.
        Guid? tradeGuid = string.IsNullOrEmpty(tradeId) || tradeId.Equals("null", StringComparison.OrdinalIgnoreCase)
            ? null
            : Guid.Parse(tradeId);

        var body = JsonSerializer.Serialize(new { tradeId = tradeGuid }, JsonOptions);
        return await client.PatchJsonAsync(
            $"/api/decisions/{Uri.EscapeDataString(decisionId)}/link-trade",
            body,
            cancellationToken).ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
