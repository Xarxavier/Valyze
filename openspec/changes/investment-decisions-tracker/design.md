# Design: investment-decisions-tracker

## Architecture decisions resolved

The proposal flagged six open questions and the orchestrator delegated their resolution to this phase. They are resolved below; the rest of the design assumes these as fixed.

### AD-1 — `linked_trade_id` FK uses `ON DELETE SET NULL` (deliberate divergence from CASCADE)

**Decision.** The FK from `investment_decisions.linked_trade_id` to `trades.id` is configured `ON DELETE SET NULL`. This is the **only** non-CASCADE FK in the schema today.

**Rationale.** Decisions outlive trades. A decision is the *intent* (we wrote down a thesis), the trade is the *execution*. A user cleaning up a duplicate or re-importing a corrected PDF must not silently destroy the decision history that powered the track-record. If the trade row goes, the decision goes back to the "unlinked" state and can be re-linked manually — the auditable record of *what we decided and why* is preserved.

**EF Core configuration snippet** (canonical syntax in `InvestmentDecisionConfiguration : IEntityTypeConfiguration<InvestmentDecision>`):

```csharp
builder.Property(d => d.LinkedTradeId)
    .HasColumnName("linked_trade_id")
    .IsRequired(false);

builder.HasOne<Trade>()
    .WithMany()
    .HasForeignKey(d => d.LinkedTradeId)
    .IsRequired(false)
    .OnDelete(DeleteBehavior.SetNull);

builder.HasIndex(d => d.LinkedTradeId)
    .HasDatabaseName("ix_investment_decisions_linked_trade_id");
```

The `account_id` FK keeps the existing CASCADE pattern (matches `TradeConfiguration` line 25). The migration must be diff-checked to confirm `ON DELETE SET NULL` lands in the generated SQL — `IsRequired(false)` is non-negotiable, EF refuses `SetNull` on a non-nullable property.

### AD-2 — `±5%` evaluation threshold via `IOptions<DecisionEvaluationOptions>`

**Decision.** The threshold lives in `appsettings.json` under `Decisions:Evaluation:AchievementThreshold`, default `0.05` (5%). Bound at startup via `services.Configure<DecisionEvaluationOptions>(configuration.GetSection("Decisions:Evaluation"))` and injected as `IOptions<DecisionEvaluationOptions>` into `EvaluateDecisionUseCase` and `GetDecisionTrackRecordUseCase`.

**Rationale.** Self-hosted operators tune this without recompiling. Keeps the use case pure — the threshold is data, not policy. `IOptionsMonitor` is overkill here (no hot-reload requirement); plain `IOptions` is enough.

```csharp
public sealed class DecisionEvaluationOptions
{
    public decimal AchievementThreshold { get; set; } = 0.05m;
}
```

### AD-3 — HOLD without instrument returns `NOT_APPLICABLE`, not `MIXED`

**Decision.** Add `NOT_APPLICABLE = 5` to `DecisionStatus`. Final enum: `PENDING_HORIZON=1 | ACHIEVED=2 | UNDERPERFORMING=3 | MIXED=4 | NOT_APPLICABLE=5`.

**Rationale.** `MIXED` is reserved for multi-leg outcomes (REBALANCE where some legs win and some lose). Overloading it with "no instrument therefore no math" buries a clean signal under an ambiguous one. `NOT_APPLICABLE` returns a single, honest answer: *we cannot evaluate this row*. It also covers the "price feed had no quote at decision time" branch (AD-4), which is the same semantic class — *cannot compute*, not *computed and mixed*.

`DecisionStatus` is **not stored** in the column set; it's computed at evaluation time. Adding the enum value is a Domain-only change.

### AD-4 — Price-feed failure is fail-soft

**Decision.** `record_decision` calls `GET /api/market/price?symbol={isin}` synchronously **before** the `INSERT`. If the endpoint returns 404 / 5xx / no quote, the decision is persisted with `price_at_decision_amount = NULL` AND `price_at_decision_currency = NULL`. The endpoint returns `201 Created` with a body that includes a non-null `warning` field describing the gap. Later, `evaluate_decision` sees the NULL pair and returns `status = NOT_APPLICABLE` with `message = "price unavailable at decision time"`.

**Rationale.** The journal entry has standalone value (rationale, source, action, horizon, timestamp) even without a price snapshot. Refusing to write the row punishes the user for a flaky feed they don't control. Surfacing the gap via `warning` keeps the truth visible — the AI can see it on the create response and tell the user.

The two snapshot columns are **both** NULL or **both** set — never one without the other. Enforced as a domain invariant in `InvestmentDecisionEntity` and checked in unit tests.

### AD-5 — JSON casing on the API: camelCase

**Decision.** All request and response bodies on `/api/decisions/*` and `/api/market/price` use camelCase JSON. C# DTOs are PascalCase; `System.Text.Json` defaults handle the conversion.

**Rationale.** Matches `NewsEndpoints.cs` (`urlTemplate`, `pollingIntervalMinutes`, `lastPolledAt`) and `PortfolioEndpoints` (`avgCost`, `unrealizedPnl`, `valuationCoverage`). Frontend consumers (Tauri) and the MCP tool layer expect that contract — diverging here would force a custom serializer for one feature.

### AD-6 — Concurrency within a chat turn: serialized by the model, no API-side locks

**Decision.** Document as an assumption in the spec: the MCP tool dispatch is serial — the model issues one tool call, awaits the response, then issues the next. We do **not** add row-level locks, optimistic concurrency tokens, or transaction-scoped coordination at the API layer for `record_decision` / `link-trade`.

**Rationale.** Today's transport (stdio JSON-RPC, one client per session) makes parallel tool calls within a single chat turn impossible by construction. Building a defence-in-depth layer for a scenario the protocol prevents is YAGNI. If multi-client concurrency lands later (e.g. server-side suggestion worker also writing decisions), revisit with proper version tokens — but that's a new SDD, not this one.

The two writes that *could* race across separate clients (`record_decision` + `link-trade` for the same decision) are independent: `link-trade` is idempotent (writes `linked_trade_id`, last writer wins, and that's fine because the user explicitly confirmed the link).

---

## Schema

### Postgres DDL (logical)

```sql
CREATE TABLE investment_decisions (
    id                          uuid PRIMARY KEY,
    account_id                  uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    source                      smallint NOT NULL,            -- DecisionSource enum (1..5)
    action                      smallint NOT NULL,            -- DecisionAction enum (1..4)
    isin                        text NULL,                    -- nullable: pure HOLD/REBALANCE journal entry
    ticker                      text NULL,                    -- secondary lookup key
    quantity_amount             numeric(28, 8) NULL,          -- meaning depends on quantity_units
    quantity_currency           char(3) NULL,                 -- only set when units = AMOUNT_BASE_CCY
    quantity_units              smallint NOT NULL,            -- QuantityUnits enum (1..3)
    price_at_decision_amount    numeric(28, 8) NULL,          -- both NULL together (fail-soft)
    price_at_decision_currency  char(3) NULL,                 -- both NULL together
    rationale                   text NOT NULL,
    evaluation_horizon_days     int NOT NULL,                 -- defaulted by action when caller omits
    ai_chat_session_id          uuid NULL,                    -- populated by SDD #3 chat-persistence-DB
    linked_trade_id             uuid NULL REFERENCES trades(id) ON DELETE SET NULL,
    source_other_note           text NULL,                    -- only when source = OTHER
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz NOT NULL DEFAULT now()
);
```

### Indexes

```sql
CREATE INDEX ix_investment_decisions_account_created
    ON investment_decisions (account_id, created_at DESC);

CREATE INDEX ix_investment_decisions_account_source_action_created
    ON investment_decisions (account_id, source, action, created_at);

CREATE INDEX ix_investment_decisions_account_isin
    ON investment_decisions (account_id, isin) WHERE isin IS NOT NULL;

CREATE INDEX ix_investment_decisions_linked_trade_id
    ON investment_decisions (linked_trade_id) WHERE linked_trade_id IS NOT NULL;
```

**Note on the dropped `(account_id, source, status)` index.** The proposal had it. We are dropping it because **`status` is computed, not stored** — there is no column to index. The track-record aggregation reads `(account_id, source, action, created_at)` plus the price snapshot, computes status row-by-row, and groups in memory or via a single SQL `CASE` expression. The replacement index `ix_investment_decisions_account_source_action_created` covers the aggregation query path without forcing a denormalized status column we'd then have to keep in sync on every `evaluate_decision` call.

If track-record performance ever becomes a bottleneck (10k+ decisions per account), revisit by adding a `cached_status` column populated on a periodic worker — that's option (a) from the proposal note. **For v1 we ship option (b): no status column, computed live.**

### Migration

- Name: `AddInvestmentDecisionsTable`.
- Reversible: `Down()` drops the four indexes and the table.
- Verify generated SQL contains `ON DELETE SET NULL` for `linked_trade_id` FK (manual diff inspection — the `--verbose` flag on `dotnet ef migrations add` shows it).
- Migration comment for `ai_chat_session_id`: `-- Populated by SDD #3 (chat-persistence-DB). NULL in v1.`

---

## Domain model

### Enums (all in `Valyze.Domain/Enum/`)

```csharp
public enum DecisionAction : short
{
    Buy = 1,
    Sell = 2,
    Hold = 3,
    Rebalance = 4,
}

public enum DecisionSource : short
{
    AiRecommendation = 1,
    UserOwnAnalysis = 2,
    ExternalNews = 3,
    ThirdPartyTip = 4,
    Other = 5,
}

public enum QuantityUnits : short
{
    Shares = 1,
    AmountBaseCcy = 2,
    PercentPortfolio = 3,
}

public enum DecisionStatus : short
{
    PendingHorizon = 1,
    Achieved = 2,
    Underperforming = 3,
    Mixed = 4,
    NotApplicable = 5,
}
```

`DecisionStatus` is **returned only**, never persisted. The other three are persisted as `short` via the EF mapper cast pattern (matches `NewsSource.Scope`).

### `InvestmentDecisionEntity` (`Valyze.Domain/Entities/Decisions/`)

```csharp
public sealed class InvestmentDecisionEntity
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public DecisionSource Source { get; set; }
    public DecisionAction Action { get; set; }
    public string? Isin { get; set; }
    public string? Ticker { get; set; }
    public decimal? QuantityAmount { get; set; }
    public Currency? QuantityCurrency { get; set; }   // only when units = AmountBaseCcy
    public QuantityUnits QuantityUnits { get; set; }
    public Money? PriceAtDecision { get; set; }       // null when feed had no quote
    public string Rationale { get; set; } = null!;    // non-empty invariant
    public int EvaluationHorizonDays { get; set; }
    public Guid? AiChatSessionId { get; set; }
    public Guid? LinkedTradeId { get; set; }
    public string? SourceOtherNote { get; set; }       // only when Source = Other
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

POCO with public setters — matches `TradeEntity`. Validation lives in the use cases.

### Domain VOs (`Valyze.Domain/Decisions/`)

```csharp
public sealed record DecisionEvaluation(
    DecisionStatus Status,
    decimal? ReturnPercent,        // null when status is PENDING_HORIZON or NOT_APPLICABLE
    int DaysElapsed,
    int Horizon,
    Money? PriceThen,              // null when no snapshot
    Money? PriceNow,               // null when no current quote
    string? Message);              // human-readable reason for NOT_APPLICABLE

public sealed record DecisionTrackRecord(
    IReadOnlyList<DecisionTrackRecordRow> BySource);

public sealed record DecisionTrackRecordRow(
    DecisionSource Source,
    int Total,
    int Achieved,
    int Underperforming,
    int Pending,
    int NotApplicable,
    int Mixed,
    decimal? AvgReturnPercent);    // null when no resolved decisions yet
```

---

## Use cases (interfaces in `Domain/Application/Decisions/`)

All five interfaces live in a single file `IDecisionUseCases.cs` (matches existing convention — see how news use cases are co-located).

```csharp
public interface IRecordDecisionUseCase
{
    Task<RecordDecisionResult> ExecuteAsync(
        RecordDecisionCommand command,
        CancellationToken ct = default);
}

public sealed class RecordDecisionCommand
{
    public Guid AccountId { get; set; }
    public DecisionSource Source { get; set; }
    public DecisionAction Action { get; set; }
    public string? Isin { get; set; }
    public string? Ticker { get; set; }
    public decimal? QuantityAmount { get; set; }
    public Currency? QuantityCurrency { get; set; }
    public QuantityUnits QuantityUnits { get; set; }
    public string Rationale { get; set; } = null!;
    public int? EvaluationHorizonDays { get; set; }   // null => default per action
    public string? SourceOtherNote { get; set; }
}

public sealed record RecordDecisionResult(Guid Id, string? Warning);

public interface IListDecisionsUseCase
{
    Task<IReadOnlyList<InvestmentDecisionEntity>> ExecuteAsync(
        ListDecisionsQuery query,
        CancellationToken ct = default);
}

public sealed class ListDecisionsQuery
{
    public Guid AccountId { get; set; }
    public int? Limit { get; set; }                   // default 50, max 500
    public DateTimeOffset? Since { get; set; }
    public DecisionSource? Source { get; set; }
    public DecisionAction? Action { get; set; }
    public string? Isin { get; set; }
}

public interface IEvaluateDecisionUseCase
{
    Task<DecisionEvaluation> ExecuteAsync(
        Guid decisionId,
        Guid accountId,
        CancellationToken ct = default);
}

public interface IGetDecisionTrackRecordUseCase
{
    Task<DecisionTrackRecord> ExecuteAsync(
        Guid accountId,
        DecisionSource? sourceFilter,
        CancellationToken ct = default);
}

public interface ILinkDecisionToTradeUseCase
{
    Task ExecuteAsync(
        Guid decisionId,
        Guid accountId,
        Guid? tradeId,
        CancellationToken ct = default);
}
```

**Default-horizon resolution lives in `RecordDecisionUseCase`**, not in the entity:

| Action | Default horizon (days) |
|---|---|
| `Buy` | 180 |
| `Sell` | 30 |
| `Hold` | 90 |
| `Rebalance` | 90 |

The mapping is a static method on the use case (`ResolveDefaultHorizon(DecisionAction)`), exercised directly by unit tests.

---

## Repository (write-side, `Domain/Repository/`)

```csharp
public interface IInvestmentDecisionRepository
{
    Task<Guid> CreateAsync(
        InvestmentDecisionEntity decision,
        CancellationToken ct = default);

    Task UpdateLinkedTradeAsync(
        Guid decisionId,
        Guid accountId,           // tenant guard at the repo layer
        Guid? tradeId,
        CancellationToken ct = default);

    Task<InvestmentDecisionEntity?> GetByIdForAccountAsync(
        Guid id,
        Guid accountId,
        CancellationToken ct = default);
}
```

Implementation `InvestmentDecisionRepository` (`Valyze.Infraestructure.Repository/Decisions/`) — EF Core, scopes every query by `AccountId`, mirrors `TradeRepository`. `UpdateLinkedTradeAsync` does an EF tracked update, `SaveChangesAsync`, throws `BusinessException` if the row doesn't exist or belongs to a different account.

---

## Query services (read-side, `Domain/QueryService/`)

```csharp
public interface IInvestmentDecisionQueryService
{
    Task<IReadOnlyList<InvestmentDecisionEntity>> ListByAccountAsync(
        Guid accountId,
        ListDecisionsQuery query,
        CancellationToken ct = default);

    Task<DecisionTrackRecord> GetTrackRecordAsync(
        Guid accountId,
        DecisionSource? sourceFilter,
        decimal achievementThreshold,
        CancellationToken ct = default);
}
```

Implementation `InvestmentDecisionQueryService : BaseQueryService` (`Valyze.Infraestructure.QueryService/Decisions/`) — Dapper, parameterized `@AccountId`, post-validates with `AccountGuard.EnforceMany`. The track-record query reads `investment_decisions` joined with the latest `price_quotes` row per `isin` and computes status via a SQL `CASE` expression keyed by elapsed days, current vs snapshot price, and the threshold parameter.

---

## API surface (`Valyze.Host`)

All routes under `MapGroup("/api/decisions").RequireAuthorization()`. `AccessorClassEntity accessor` injected into every handler — `accessor.AccountId` flows to use cases.

| Verb / Path | Body / Query | Response |
|---|---|---|
| `POST /api/decisions` | `{ source, action, isin?, ticker?, quantity?, quantityCurrency?, units, rationale, horizonDays?, sourceOtherNote? }` | `201 { id, warning? }` |
| `GET /api/decisions` | `?limit&since&source&action&isin` | `200 { count, decisions: [...] }` |
| `GET /api/decisions/{id}/evaluate` | — | `200 { status, returnPercent?, daysElapsed, horizon, priceThen?, priceNow?, message? }` |
| `GET /api/decisions/track-record` | `?source` | `200 { bySource: [...] }` |
| `PATCH /api/decisions/{id}/link-trade` | `{ tradeId: uuid \| null }` | `204 No Content` |

Plus a separate market endpoint (file `Valyze.Host/MinimalApi/Market/MarketEndpoints.cs`):

| Verb / Path | Query | Response |
|---|---|---|
| `GET /api/market/price` | `?symbol={isin or ticker}&currency={iso?}` | `200 { amount, currency, ts }` or `404 { reason }` |

`/api/market/price` is registered in the same `MapMinimalApiExtensions` wiring as the other groups. JWT-required. Backed by `IPriceQuoteQueryService.GetFreshAsync` with a freshness window of 24h. If no fresh quote exists, returns `404 { reason: "no fresh quote" }` — the MCP tool catches this and persists the decision with NULL snapshot.

All JSON casing: **camelCase** (AD-5).

---

## MCP tools (`Valyze.Mcp/Tools/DecisionTools.cs`)

New static class `[McpServerToolType] public static class DecisionTools`. Pattern matches `NewsTools.cs` (constructor-less, methods take injected `ValyzeApiClient`).

```csharp
[McpServerTool(Name = "record_decision")]
[Description(
    "Records an investment decision the user transmitted to you in this chat. " +
    "IMPORTANT: BEFORE calling this tool you MUST ask the user which `source` " +
    "applies and confirm with them. Never infer source from context. Valid " +
    "sources: AI_RECOMMENDATION, USER_OWN_ANALYSIS, EXTERNAL_NEWS, " +
    "THIRD_PARTY_TIP, OTHER. The tool snapshots the price at decision time " +
    "via the backend; if the price feed has no quote, the decision is still " +
    "saved with a `warning` field — surface that warning to the user. " +
    "Default horizons (when omitted): BUY=180d, SELL=30d, HOLD=90d, " +
    "REBALANCE=90d.")]
public static async Task<string> RecordDecisionAsync(
    ValyzeApiClient client,
    [Description("AI_RECOMMENDATION | USER_OWN_ANALYSIS | EXTERNAL_NEWS | THIRD_PARTY_TIP | OTHER")] string source,
    [Description("BUY | SELL | HOLD | REBALANCE")] string action,
    [Description("ISIN (preferred) of the instrument. Optional for pure REBALANCE/HOLD.")] string? isin,
    [Description("Ticker symbol if ISIN unknown. Optional.")] string? ticker,
    [Description("Quantity amount. Meaning depends on units.")] decimal? quantity,
    [Description("Currency code when units=AMOUNT_BASE_CCY. Otherwise omit.")] string? quantityCurrency,
    [Description("SHARES | AMOUNT_BASE_CCY | PERCENT_PORTFOLIO")] string units,
    [Description("Why the user is making this decision. Required.")] string rationale,
    [Description("Evaluation horizon in days. Omit for action-default.")] int? horizonDays,
    [Description("Free-text note when source=OTHER.")] string? sourceOtherNote,
    CancellationToken cancellationToken);

[McpServerTool(Name = "list_decisions")]
[Description(
    "Returns the user's recorded investment decisions, most recent first. " +
    "Filter by source / action / ISIN / since. Use this before linking a " +
    "trade to look up unlinked decisions for the same instrument.")]
public static async Task<string> ListDecisionsAsync(
    ValyzeApiClient client,
    int? limit,
    string? since,
    string? source,
    string? action,
    string? isin,
    CancellationToken cancellationToken);

[McpServerTool(Name = "evaluate_decision")]
[Description(
    "Evaluates a single decision: returns status (PENDING_HORIZON | ACHIEVED | " +
    "UNDERPERFORMING | MIXED | NOT_APPLICABLE), return percent, days elapsed " +
    "vs horizon, and the prices at decision and now. NOT_APPLICABLE means we " +
    "couldn't compute (no price snapshot, or instrument-less HOLD).")]
public static async Task<string> EvaluateDecisionAsync(
    ValyzeApiClient client,
    [Description("Decision id (UUID).")] string decisionId,
    CancellationToken cancellationToken);

[McpServerTool(Name = "get_decision_track_record")]
[Description(
    "Aggregates hit-rate per decision source: total, achieved, underperforming, " +
    "pending, average return. Use this to answer 'which channel of advice has " +
    "actually worked for me?'. Optional `source` filter narrows to one channel.")]
public static async Task<string> GetDecisionTrackRecordAsync(
    ValyzeApiClient client,
    [Description("Optional: AI_RECOMMENDATION | USER_OWN_ANALYSIS | EXTERNAL_NEWS | THIRD_PARTY_TIP | OTHER")] string? source,
    CancellationToken cancellationToken);
```

The tool docstring on `record_decision` is the **invocation-time guardrail** for source confirmation — paired with `ServerInstructions` it gives defence in depth.

---

## Persona hook (`ServerInstructions` in `Valyze.Mcp/Program.cs`)

Add a new subsection inside the existing "Tool selection guide" block, **immediately after the "News" subsection and before "Web"**. Exact text:

```text
Decision tracking (MCP):
- "Anoté que comprás X" / "registrá esta decisión" / any explicit user
  intent to log a BUY / SELL / HOLD / REBALANCE → **`record_decision`**.
  BEFORE calling, ALWAYS ask the user which `source` applies (AI
  recommendation / their own analysis / external news / third-party tip
  / other) and confirm. Never infer source from chat context — the whole
  point of the ledger is that the user owns the attribution.
- "¿Qué decisiones registramos?" / "mostrá las últimas X decisiones" →
  **`list_decisions`**.
- "¿Cómo va aquella decisión de comprar X?" / "evaluá la decisión Y" →
  **`evaluate_decision`**. Status PENDING_HORIZON means the horizon
  hasn't elapsed yet; NOT_APPLICABLE means we lack a price snapshot —
  surface the gap honestly.
- "¿De qué fuente vienen mis mejores decisiones?" / "¿el AI te ha
  funcionado?" → **`get_decision_track_record`**. This is the core
  feedback loop — be honest with the numbers, including when the AI
  channel underperforms.
- After a PDF import surfaces a new trade, ask ONCE if it matches an
  open unlinked decision (same ISIN, recent date) → on user confirmation
  call the link-trade endpoint. Don't auto-link.
```

This sits parallel to the existing "News (MCP — internal cache)" subsection. The "Tool selection guide" header stays unchanged. Keep the bullet style consistent with the surrounding text (Rioplatense Spanish triggers in quotes, English imperative for the action).

---

## Tauri allowlist

Add 4 entries to `VALYZE_MCP_TOOLS` in `tauri/src-tauri/src/claude_chat.rs`, after the News block:

```rust
const VALYZE_MCP_TOOLS: &[&str] = &[
    // Portfolio (read-only)
    "mcp__valyze__get_positions",
    "mcp__valyze__get_portfolio_summary",
    "mcp__valyze__get_trades",
    // News (read + curate)
    "mcp__valyze__get_news_for_symbol",
    "mcp__valyze__get_latest_news",
    "mcp__valyze__list_news_sources",
    "mcp__valyze__add_news_source",
    "mcp__valyze__disable_news_source",
    "mcp__valyze__refresh_news",
    // Decisions (record + read + evaluate + track-record)
    "mcp__valyze__record_decision",
    "mcp__valyze__list_decisions",
    "mcp__valyze__evaluate_decision",
    "mcp__valyze__get_decision_track_record",
];
```

No other Tauri changes — `link-trade` is a pure HTTP endpoint invoked indirectly by the persona's flow (the AI uses `list_decisions` to find the candidate and surfaces the manual confirmation prompt; the actual PATCH is fired through whatever endpoint we expose for the AI to call after the user confirms — covered by a future iteration if we promote it to its own MCP tool).

---

## Evaluation logic (pseudo-code)

`EvaluateDecisionUseCase.ExecuteAsync(decisionId, accountId, ct)`:

```text
decision = repo.GetByIdForAccount(decisionId, accountId)
if decision is null → throw BusinessException(NotFound)

if decision.PriceAtDecision is null:
    return DecisionEvaluation(
        Status = NOT_APPLICABLE,
        ReturnPercent = null,
        DaysElapsed = (now - decision.CreatedAt).TotalDays,
        Horizon = decision.EvaluationHorizonDays,
        PriceThen = null,
        PriceNow = null,
        Message = "price unavailable at decision time")

if decision.Action == HOLD and decision.Isin is null:
    return DecisionEvaluation(NOT_APPLICABLE, null, ..., "instrument-less HOLD")

daysElapsed = (now - decision.CreatedAt).TotalDays
if daysElapsed < decision.EvaluationHorizonDays:
    return DecisionEvaluation(
        Status = PENDING_HORIZON,
        ReturnPercent = null,
        DaysElapsed, Horizon,
        PriceThen = decision.PriceAtDecision,
        PriceNow = null,
        Message = null)

priceNow = priceQuoteQueryService.GetFreshAsync(
    [decision.Isin], decision.PriceAtDecision.Currency, now - 24h)
if priceNow is empty:
    return DecisionEvaluation(NOT_APPLICABLE, null, ..., "no current quote")

returnPct = (priceNow.Amount - decision.PriceAtDecision.Amount)
            / decision.PriceAtDecision.Amount

threshold = options.AchievementThreshold

favorable = decision.Action switch:
    BUY or HOLD       => returnPct >= threshold
    SELL              => returnPct <= -threshold      // we sold; we wanted price down
    REBALANCE         => MIXED        (single-leg approximation in v1)

if action is REBALANCE:
    return DecisionEvaluation(MIXED, returnPct, ...)

return DecisionEvaluation(
    Status = favorable ? ACHIEVED : UNDERPERFORMING,
    ReturnPercent = returnPct,
    DaysElapsed, Horizon,
    PriceThen = decision.PriceAtDecision,
    PriceNow = Money(priceNow.Amount, decision.PriceAtDecision.Currency),
    Message = null)
```

---

## Track-record aggregation (SQL pseudo-code)

`InvestmentDecisionQueryService.GetTrackRecordAsync(accountId, sourceFilter, threshold, ct)`:

```sql
WITH latest_quote AS (
    SELECT DISTINCT ON (isin, ccy) isin, ccy, price, ts
    FROM price_quotes
    WHERE ts > now() - interval '24 hours'
    ORDER BY isin, ccy, ts DESC
)
SELECT
    d.source,
    COUNT(*)                                                              AS total,
    SUM(CASE WHEN <evaluated as ACHIEVED>          THEN 1 ELSE 0 END)     AS achieved,
    SUM(CASE WHEN <evaluated as UNDERPERFORMING>   THEN 1 ELSE 0 END)     AS underperforming,
    SUM(CASE WHEN <horizon not yet reached>        THEN 1 ELSE 0 END)     AS pending,
    SUM(CASE WHEN <NOT_APPLICABLE>                 THEN 1 ELSE 0 END)     AS not_applicable,
    SUM(CASE WHEN d.action = 4 /* REBALANCE */ AND <horizon reached>
                                                   THEN 1 ELSE 0 END)     AS mixed,
    AVG(CASE WHEN <horizon reached AND has both prices>
             THEN (q.price - d.price_at_decision_amount)
                  / d.price_at_decision_amount END)                       AS avg_return_percent
FROM investment_decisions d
LEFT JOIN latest_quote q
    ON q.isin = d.isin
   AND q.ccy = d.price_at_decision_currency
WHERE d.account_id = @AccountId
  AND (@SourceFilter IS NULL OR d.source = @SourceFilter)
GROUP BY d.source
ORDER BY d.source;
```

The `<...>` placeholders expand to a `CASE` expression that re-applies the same arithmetic as the use case (days elapsed, threshold). The threshold travels in via Dapper as `@Threshold`. **Single Dapper round-trip** — no per-row N+1.

After fetching, the query service materializes `DecisionTrackRecordRow` and calls `AccountGuard.EnforceMany` defensively (the `WHERE` already scoped to `AccountId`, but the post-validate is the project's standard defence-in-depth).

---

## DI registration deltas

| File | Add |
|---|---|
| `Valyze.Application/ServiceExtensions.cs` | `services.AddScoped<IRecordDecisionUseCase, RecordDecisionUseCase>();` and 4 more for the other use cases. Bind `services.Configure<DecisionEvaluationOptions>(configuration.GetSection("Decisions:Evaluation"));` |
| `Valyze.Infraestructure.Repository/ServiceExtensions.cs` | `services.AddScoped<IInvestmentDecisionRepository, InvestmentDecisionRepository>();` |
| `Valyze.Infraestructure.QueryService/ServiceExtensions.cs` | `services.AddScoped<IInvestmentDecisionQueryService, InvestmentDecisionQueryService>();` |
| `Valyze.Host/MinimalApi/MapMinimalApiExtensions.cs` | `app.MapDecisionEndpoints();` and `app.MapMarketEndpoints();` |
| `Valyze.Host/Program.cs` (or extension method) | The `Configure<DecisionEvaluationOptions>` call must be reachable from Host (Application's `ServiceExtensions` is the natural home). |

DI lifetime: **Scoped** for everything (matches project convention, no exceptions).

---

## Configuration

`backend/src/Valyze.Host/appsettings.json` — add:

```json
{
  "Decisions": {
    "Evaluation": {
      "AchievementThreshold": 0.05
    }
  }
}
```

The default lives in code (`DecisionEvaluationOptions.AchievementThreshold = 0.05m`); the JSON key is optional. Operators tune by overriding in `appsettings.Production.json` or via `DECISIONS__EVALUATION__ACHIEVEMENTTHRESHOLD` env var.

---

## Tests (TDD — strict mode is ACTIVE)

Tests are written **first**, in the apply phase, in this order:

### Domain tests (`Valyze.Domain.Tests/Decisions/`)

- `DecisionEvaluationTests`:
  - PENDING_HORIZON when daysElapsed < horizon, regardless of price.
  - ACHIEVED for BUY when returnPct ≥ threshold.
  - ACHIEVED for HOLD when returnPct ≥ threshold.
  - ACHIEVED for SELL when returnPct ≤ -threshold.
  - UNDERPERFORMING for BUY when returnPct ≤ -threshold.
  - UNDERPERFORMING for SELL when returnPct ≥ threshold (price went up; bad sell).
  - Border cases at exactly ±threshold — favorable boundary inclusive.
  - REBALANCE returns MIXED after horizon (single-leg approximation).
  - NOT_APPLICABLE when PriceAtDecision is null.
  - NOT_APPLICABLE for HOLD without instrument.
  - Threshold customization (5% vs 10%).

- `InvestmentDecisionEntityTests`:
  - `AccountId` is required (Guid.Empty rejected by use case validator).
  - `Rationale` non-empty invariant (whitespace rejected).
  - Snapshot columns paired: both null OR both set, never one without the other.
  - Source = OTHER allows non-null `SourceOtherNote`; other sources reject it.

- `MoneyInvariantsOnDecisionTests`:
  - Mixed-currency arithmetic in evaluation throws (mirrors existing Money tests).

### Application tests (`Valyze.Application.Tests/Decisions/` — new project if absent, otherwise inline in `Valyze.Domain.Tests` since the project is in TDD mode and use cases live in Application but test against repo/query mocks)

- `RecordDecisionUseCaseTests`:
  - Happy path: persists decision with snapshot fetched from `IPriceQuoteQueryService`.
  - Price-feed-null path: persists decision with NULL snapshot, returns `Warning`.
  - Default horizon resolution per action (4 cases).
  - Cross-account create rejected (caller cannot impersonate another `AccountId`).

- `EvaluateDecisionUseCaseTests`:
  - Status transitions across all 5 statuses.
  - `Money` mismatch between snapshot currency and current quote currency throws.
  - Cross-account read returns `NotFound` (no info leak).

- `GetDecisionTrackRecordUseCaseTests`:
  - Aggregation across 3 sources, mixed statuses, threshold applied correctly.
  - Empty account returns empty rows (not an error).

- `LinkDecisionToTradeUseCaseTests`:
  - Happy path: link, unlink (tradeId = null).
  - Trade owned by another account is NOT linkable (404 / BusinessException).

### Multi-tenancy tests

- Dedicated `AccountIsolationTests` in `Valyze.Domain.Tests/Decisions/`:
  - Account A's `list_decisions` cannot see Account B's rows.
  - Account A cannot evaluate Account B's decision (returns NotFound, not Forbidden, to avoid existence leak).
  - Account A cannot link a Trade to Account B's decision.

These mirror the existing tenant-isolation tests on `TradeQueryService`.

---

## Risks

Carried forward from explore + proposal, and new ones surfaced here:

1. **`ON DELETE SET NULL` migration verification** — must inspect generated SQL after `dotnet ef migrations add AddInvestmentDecisionsTable`. If EF emits `Restrict` or `Cascade`, the configuration is wrong. Mitigation: TDD-style integration check — write a small migration assertion test reading the migration `.cs` file, OR a manual diff in apply phase.

2. **Status-as-CASE in track-record SQL is not trivial** — the `CASE` expression must implement the same logic as the use case, in SQL. Risk of drift between the two implementations. Mitigation: a single integration test that seeds known data and asserts `GetTrackRecordAsync` agrees with the row-by-row use case output. Add this to the test suite as a regression guard.

3. **`ai_chat_session_id` column ships unpopulated** — every row will have NULL until SDD #3 lands. If SDD #3 changes its mind about column shape (e.g., wants a string, not a UUID), we have a wasted column. Mitigation: low-cost — drop and re-add the column in SDD #3 if the type changes. UUID is the safest bet given chat-session UUIDs are already the model in `tauri/src-tauri/src/chat_storage.rs`.

4. **`DecisionStatus` on the wire** — the API returns the enum as a string for readability (matches `news_sources.scope`). The MCP tool docstrings document the string form. Risk: any future enum addition is a wire-level change visible to clients. Mitigation: documented in proposal — never reorder, only append.

5. **Price snapshot currency mismatch** — `record_decision` fetches a quote in *some* currency. If that doesn't match the user's account base, we still snapshot the native quote currency. Evaluation requires fetching the current quote in the *same* currency as the snapshot. Mitigation: covered by the Money invariant tests; the evaluation throws on mismatch which surfaces as a clear error rather than silent corruption. Long-term, FX conversion at evaluation time is a sensible v2 enhancement.

6. **`/api/market/price` rate limiting / abuse** — the endpoint is JWT-protected and read-only, but a malicious or buggy client could hammer it. Mitigation: not addressed in v1 (single-tenant, single-user). When SaaS mode lands, add per-account rate limiting at the middleware layer.

7. **`PERCENT_PORTFOLIO` directional-only evaluation** — known v1 limitation; documented in the MCP tool description.

8. **Evaluation threshold calibration** — 5% might be too tight for short horizons (SELL at 30 days) and too loose for long (BUY at 180 days). Mitigation: the option is per-deployment, not per-action. v2 could promote it to a per-action map (`Decisions:Evaluation:Thresholds:Buy = 0.10`).

---

## Project Standards (auto-resolved)

### Backend code (.cs)
- Clean Architecture + CQRS-light. Domain owns contracts. Strict deps `Host → Application → Domain`. Infraestructure.* implements Domain ports.
- Naming: `{Name}Entity`, `I{Action}UseCase` / `{Action}UseCase`, `I{Aggregate}Repository` / `{Aggregate}Repository`, `I{Domain}QueryService` / `{Domain}QueryService`, `{Feature}Endpoints` + `Map{Feature}Endpoints`.
- DI: Scoped only (no singletons except framework). Spanish "Infraestructure" spelling intentional.
- Use case interfaces live in `Domain/Application/{Feature}/`. NOT in the Application project.
- Entities = POCOs with public set; behavior in use cases. Money/Currency/Isin = rich VOs.
- `Money(decimal, Currency)`. Mismatched currency arithmetic throws. NEVER bare decimal for money. Columns `numeric(28,8)`.
- ISIN is primary instrument key; tickers keyed by `(isin, exchange)`.
- Multi-tenancy: `AccountId` on every user-owned entity. Repos filter; queries post-validate via `AccountGuard.EnforceSingle/EnforceMany`.
- P&L reports three faces: native, account, FX attribution.
