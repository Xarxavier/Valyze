using System.ComponentModel;
using ModelContextProtocol.Server;
using Valyze.Mcp.Backend;

namespace Valyze.Mcp.Tools;

/// <summary>
/// Tools the AI can call to query the user's Valyze portfolio.
///
/// CONVENTIONS
///   * Each method is a tool. Description attributes are what the model
///     sees when deciding whether to invoke it — write them like a
///     usage doc, not like internal code comments.
///   * Tools return JSON strings: the model reads them, the protocol
///     wraps them in a text content block.
///   * Add a new tool by adding a method here (or a new
///     [McpServerToolType] class) — Program.cs picks it up via
///     WithToolsFromAssembly().
/// </summary>
[McpServerToolType]
public static class PortfolioTools
{
    [McpServerTool(Name = "get_positions")]
    [Description(
        "Returns the user's current portfolio positions as JSON. Includes per-position " +
        "quantity, average cost, current market value, unrealized P&L (gross and net of " +
        "broker sell commission), realized P&L, and a portfolio-level summary (total " +
        "invested, current value, P&L, valuation coverage, base currency). Use this to " +
        "answer any question about what the user holds, exposure, or P&L.")]
    public static async Task<string> GetPositionsAsync(
        ValyzeApiClient client,
        CancellationToken cancellationToken)
    {
        return await client.GetPositionsAsync(cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "get_portfolio_summary")]
    [Description(
        "Returns lightweight portfolio totals (account id, base currency, total invested, " +
        "position count, trade count, foreign-currency invested totals). Cheaper than " +
        "get_positions when only aggregates are needed.")]
    public static async Task<string> GetPortfolioSummaryAsync(
        ValyzeApiClient client,
        CancellationToken cancellationToken)
    {
        return await client.GetPortfolioAsync(cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "get_trades")]
    [Description(
        "Returns the flat list of executed trades, optionally filtered by instrument symbol " +
        "(ISIN for securities, ticker for crypto — case-insensitive). Each trade includes " +
        "executedAt, side (Buy/Sell), quantity, price, fees, broker, and the broker reference. " +
        "Use this to reason about execution history, average cost evolution, or specific fills.")]
    public static async Task<string> GetTradesAsync(
        ValyzeApiClient client,
        [Description("Optional instrument symbol to filter by (ISIN or ticker, case-insensitive). Omit to return all trades across the portfolio.")]
        string? symbol,
        CancellationToken cancellationToken)
    {
        return await client.GetTradesAsync(symbol, cancellationToken).ConfigureAwait(false);
    }
}
