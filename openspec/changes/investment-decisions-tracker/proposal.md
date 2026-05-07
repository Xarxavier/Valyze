# Proposal: investment-decisions-tracker

## Why

The new co-pilot persona (engram #97) is opinionated by design — it gives concrete tickers, allocations, and reasoning. That promise is hollow without a measurable accountability loop. Today nothing in Valyze tells the user whether the AI's calls have been right, whether the user's own analysis beats it, or whether tips from third parties consistently disappoint.

This change introduces an **investment decisions ledger**: every BUY / SELL / HOLD / REBALANCE the user explicitly transmits is recorded with a user-confirmed `source` (AI / own analysis / news / third-party tip / other), a price snapshot, and a horizon. Later we evaluate each decision and aggregate hit-rate by source. The output feeds back into persona refinement and gives the user objective evidence about which decision channel actually produces results.

Trades imported from PDFs are NOT auto-interpreted as decisions — execution is not intent. The bridge between the two is manual and user-confirmed.

## What changes

- New `investment_decisions` table with multi-tenant isolation by `AccountId`, FK to `accounts` (CASCADE) and nullable FK to `trades` (SET NULL).
- Two new domain enums: `DecisionAction` (BUY / SELL / HOLD / REBALANCE) and `DecisionSource` (AI_RECOMMENDATION / USER_OWN_ANALYSIS / EXTERNAL_NEWS / THIRD_PARTY_TIP / OTHER), both with explicit numeric stability for safe persistence as `short`.
- New backend endpoints under `/api/decisions` (`POST`, `GET list`, `GET {id}/evaluation`, `GET track-record`, `PATCH {id}/link-trade`).
- New dedicated read-only endpoint `GET /api/market/price?symbol={isin}` — used by the MCP tool to snapshot price-at-decision; reusable by the future suggestion pipeline.
- 4 new MCP tools in `Valyze.Mcp/Tools/DecisionTools.cs`: `record_decision`, `list_decisions`, `evaluate_decision`, `get_decision_track_record`.
- Persona update in `Valyze.Mcp/Program.cs` `ServerInstructions`: a new "Decision tracking (MCP)" subsection under "Tool selection guide", parallel to Portfolio / News / Web. Tells the model to ALWAYS confirm `source` with the user before invoking `record_decision`.
- Tool docstring on `record_decision` reinforces the same rule at invocation time (defence in depth).
- Tauri allowlist update: 4 new entries in `VALYZE_MCP_TOOLS` in `tauri/src-tauri/src/claude_chat.rs`.
- TDD-first: domain unit tests cover Money invariants on the new entity, evaluation logic, status transitions, and horizon defaults.

## Impact

Affected projects in the .NET solution:

- `Valyze.Domain` — new enums, entity, use case interfaces, repository port, query service port.
- `Valyze.Application` — use case implementations + `ServiceExtensions` registration.
- `Valyze.Infraestructure.EntityFramework` — EF entity, configuration, mapper, `DbSet` on `ValyzeDbContext`, new migration.
- `Valyze.Infraestructure.Repository` — `InvestmentDecisionRepository` + `ServiceExtensions` registration.
- `Valyze.Infraestructure.QueryService` — `InvestmentDecisionQueryService` + `ServiceExtensions` registration.
- `Valyze.Host` — `DecisionEndpoints`, `MarketEndpoints` (or extension to existing endpoints), `MapMinimalApiExtensions` registration.
- `Valyze.Mcp` — `Tools/DecisionTools.cs`, `Program.cs` `ServerInstructions` block.
- `Valyze.Domain.Tests` — `Decisions/` test folder.

Affected files in Tauri shell:

- `tauri/src-tauri/src/claude_chat.rs` — add 4 entries to the `VALYZE_MCP_TOOLS` allowlist.

Docs / `CLAUDE.md` sections that need updating:

- "Architecture" → "Domain Rules": add a brief note that `InvestmentDecisionEntity` follows the same multi-tenancy + `Money` split-column pattern as `TradeEntity`.
- "AI Layer" → "Flavor 1 — Local desktop chat" → "Skills" / new "Decision tracking" mini-section: document that the assistant records user decisions via MCP, must always ask for `source` before calling `record_decision`, and surfaces track-record on request.
- "Persistence (Postgres)" minimal-schema spine: add `investment_decisions` row.

## Approach

Anchored in the locked answers from explore (#110):

- **Source enum** — 5 values, user-picked, AI MUST ask. Persona enforces this in BOTH the tool docstring (invocation-time guardrail) and `ServerInstructions` (planning-time instruction).
- **Action enum** — 4 values: BUY / SELL / HOLD / REBALANCE. Stored as `short` with explicit numeric values for migration stability.
- **Schema highlights**:
  - `account_id UUID NOT NULL` (FK accounts CASCADE) — multi-tenancy spine.
  - `linked_trade_id UUID NULL` (FK trades SET NULL) — manual confirmation only.
  - `ai_chat_session_id UUID NULL` — column added now, populated later by SDD #3 (chat-persistence-DB). Not populated in v1.
  - Money columns: `price_at_decision_amount numeric(28,8)`, `price_at_decision_currency char(3)` — split-column pattern matching `TradeEntity`. `Money` is a VO at the domain boundary, not a composite SQL type.
  - `quantity_amount numeric(28,8)`, `quantity_currency char(3) NULL` (only for AMOUNT_BASE_CCY mode), and a `units` enum (`SHARES` / `AMOUNT_BASE_CCY` / `PERCENT_PORTFOLIO`).
  - Index on `(account_id, source, status)` baked into the migration from day one — required for `get_decision_track_record` to scale.
- **Manual trade-linkage flow** — no heuristic, no auto-match. After PDF import, the persona instructs the model to ask once per session whether a recently imported trade matches an open unlinked decision. User confirms → `PATCH /api/decisions/{id}/link-trade`. Heuristic auto-suggest is explicitly deferred.
- **Dedicated `GET /api/market/price?symbol={isin}` endpoint** — thin, read-only, internal. Returns `{ amount, currency, ts }` from `IPriceQuoteQueryService`. The MCP `record_decision` tool calls it synchronously BEFORE persisting. If the price feed returns no quote, the decision is still saved with NULL snapshot columns and `evaluate_decision` reports "price unavailable at decision time" rather than dividing by zero.
- **On-demand evaluation, no snapshot table in v1** — `evaluate_decision` runs the math live: `(currentPrice - priceAtDecision) / priceAtDecision`. No periodic snapshot worker, no `decision_evaluations` history table. That stays out for v1; future track-record charts can add it without schema upheaval.
- **Status enum**: `PENDING_HORIZON` / `ACHIEVED` / `UNDERPERFORMING` / `MIXED`. Threshold ±5% (configurable via `IOptions`). `MIXED` is wired into the enum but in v1 only used for REBALANCE-without-instrument; multi-leg `MIXED` waits on `decision_legs`.
- **Default horizons** (used when caller does not supply one): `BUY = 180`, `SELL = 30`, `HOLD = 90`, `REBALANCE = 90`.
- **Persona hook** — both the `record_decision` tool docstring AND the `ServerInstructions` "Decision tracking (MCP)" subsection. Two lines of text, defence in depth.

## Risks & rollback

- **`ON DELETE SET NULL` on `linked_trade_id`** — diverges from the existing project pattern (every other FK in the schema is CASCADE). EF Core supports it but requires the FK property nullable AND explicit configuration in `InvestmentDecisionConfiguration`. Migration must be reviewed to confirm SQL emits `ON DELETE SET NULL`. Calling this out so design phase doesn't sleepwalk into the default.
- **`PERCENT_PORTFOLIO` directional-only evaluation** — known v1 limitation. Without storing portfolio value at decision time, percent-mode decisions can only be evaluated as "did the instrument move up or down". Documented in the spec and surfaced in the MCP tool description.
- **New `/api/market/price` endpoint surface area** — low risk: read-only, JWT-required, internal use by MCP. No external contract.
- **Index on `(account_id, source, status)`** — MUST land in the initial migration. Adding it later means a backfill migration on a table that's already growing; doing it upfront is free.
- **Enum numeric stability** — `DecisionAction` and `DecisionSource` values must be explicitly assigned integers (`= 1`, `= 2`, …). Future additions append; never reorder.
- **Rollback plan** — purely additive change. Drop the migration (`dotnet ef migrations remove`), revert DI registrations, remove the 4 entries from `VALYZE_MCP_TOOLS`, revert `ServerInstructions`. No existing data is touched. No breaking changes to other features.

## Out of scope (deferred)

- Benchmark-relative evaluation (decision return vs SPX / sector ETF).
- `decision_legs` table for multi-leg REBALANCE decisions.
- Periodic snapshot table (`decision_evaluations` history) for track-record charts over time.
- Heuristic auto-suggest of trade ↔ decision matches (background job).
- Population of `ai_chat_session_id` — column ships nullable now, SDD #3 (chat-persistence-DB) will populate it.
- UI screen for decisions in the Tauri frontend — v1 surfaces decisions purely through the chat and MCP tools.
- Server-side `Suggestion` worker emitting `record_decision` calls — that's the future Flavor 2 AI pipeline, not this change.

## Acceptance criteria (high-level — full Given/When/Then in spec phase)

- [ ] User can record a decision via MCP and the `source` dimension is required + user-confirmed (verified by tool docstring + `ServerInstructions`).
- [ ] Decisions belong to exactly one account; cross-tenant access is impossible (`AccountGuard.EnforceMany` covers query-service path; repo filters cover EF path).
- [ ] `record_decision` snapshots price-at-decision via `GET /api/market/price` before persisting; if the price feed has no quote, the decision still persists with NULL snapshot.
- [ ] `evaluate_decision` returns `{ status, returnPercent, daysElapsed, horizon, priceThen, priceNow }` with the horizon gate honored (status `PENDING_HORIZON` while `daysElapsed < horizon`).
- [ ] `get_decision_track_record` returns aggregate hit-rate per source, never crossing accounts.
- [ ] `dotnet test` green; new domain tests cover Money invariants, evaluation math, status transitions at horizon boundary, threshold handling, and default horizon resolution per action.
- [ ] Migration is reversible (`dotnet ef database update <previous>` succeeds).
- [ ] Index `IX_investment_decisions_account_source_status` is present in the migration.

## Open questions for design phase

- **Where exactly does the "ask the user for source" instruction live in `ServerInstructions`?** Proposed: a new "Decision tracking (MCP)" subsection inside the existing "Tool selection guide", parallel to Portfolio / News / Web. Design phase confirms the exact wording and placement.
- **Does `record_decision` call `/api/market/price` before or after persisting?** Proposed: synchronously BEFORE. If the price endpoint fails (network, no quote), persist anyway with NULL snapshot and surface a warning in the MCP tool result. Design phase confirms whether failure should ever block the write.
- **Concurrency: can the same chat-turn record two decisions in parallel?** Proposed: no — the MCP tool is invoked serially by the model; we don't need cross-call locking in v1.
- **DTO shape on the API: snake_case vs camelCase?** Match the existing pattern from `NewsEndpoints` (camelCase JSON, PascalCase C# properties via System.Text.Json defaults). Design phase confirms.
- **Threshold configuration surface** — does ±5% live in `appsettings.json` via `IOptions<DecisionEvaluationOptions>`, or as a hard-coded constant in the use case? Design phase decides; default position is `IOptions` so operators can tune without recompiling.
- **`evaluate_decision` for HOLD with no instrument** — explore says return `MIXED`. Design phase confirms whether that's the right status for a no-instrument case (vs a new `NOT_APPLICABLE`) given that `MIXED` is intended for multi-leg outcomes.

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
