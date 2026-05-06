# Explore: investment-decisions-tracker

## Goal & scope (from locked decisions)

Record every investment decision (BUY / SELL / HOLD / REBALANCE) that the user explicitly transmits to the AI. Tagged with a user-picked **source** dimension (5 values). Exposed via MCP tools so the AI can record and evaluate decisions later. Measures track-record by origin and feeds back into persona refinement. Trades arriving via PDF are NOT auto-interpreted as decisions.

Source enum (user picks, AI MUST ask before saving):
- `AI_RECOMMENDATION` — concrete suggestion from the Valyze AI in chat.
- `USER_OWN_ANALYSIS` — user's own analysis, no external input.
- `EXTERNAL_NEWS` — triggered by news, podcast, public research.
- `THIRD_PARTY_TIP` — recommendation from another person, broker, paid research.
- `OTHER` — fallback with optional free-text.

---

## Open questions resolved

### Q1 — Linkage to `trades` (nullable FK `linked_trade_id`)

**Recommendation: Manual confirmation flow for v1.**

Rationale for rejecting heuristic auto-match:
- Heuristic matching (same ISIN + side + quantity ±tolerance + date window) requires tuning four parameters with no ground truth — false positives silently corrupt the track record.
- Users often decide to buy 10 shares and then execute 8 (partial fill), buy across multiple days (DCA), or use a limit order that fills days later. Tolerance-window math becomes ad hoc.
- The investment decision captures *intent*, the trade captures *execution*. These are semantically distinct. The user is the only reliable bridge between them.

Recommended v1 flow:
- After a trade is imported via PDF, the AI (during next chat) calls `list_decisions` filtering by ISIN and date range. If unlinked decisions exist for the same instrument, it asks once: "¿Esta compra de 10 BTC del martes matchea la decisión que registramos el lunes de comprar BTC?" The user confirms → the API call updates `linked_trade_id`.
- This is one line in the existing chat persona, not a new UI screen.
- Schema: `linked_trade_id UUID NULL` FK → `trades.id` with `ON DELETE SET NULL` (trade deleted doesn't destroy the decision record).

Future heuristic auto-suggest (not v1): after the manual flow is proven, a background job can propose matches as suggestions. User reviews, not auto-commits.

### Q2 — Evaluation model v1 (on-demand, no snapshots)

**Confirm defaults with refinement:**

- `BUY`: horizon = 180 days. Rationale: typical medium-term thesis window; short enough to give feedback, long enough for the idea to play out.
- `SELL`: horizon = 30 days. Rationale: after a sell, the relevant question is "did the position continue falling?" within the near term — longer is noise.
- `HOLD`: horizon = 90 days. Rationale: a hold reaffirms the thesis quarterly.
- `REBALANCE`: horizon = 90 days. Rationale: allocation changes need time to settle, but not as long as a new position.

Status enum:
- `PENDING_HORIZON` — horizon not reached yet.
- `ACHIEVED` — price moved favorably beyond a threshold (suggest ±5% absolute, configurable).
- `UNDERPERFORMING` — price moved unfavorably beyond threshold.
- `MIXED` — reserved for REBALANCE decisions where some legs performed, some didn't (v1 can just use ACHIEVED/UNDERPERFORMING for simplicity; MIXED stays in the enum for future).

Inputs:
- `price_at_decision` (snapshot stored at record time, required for BUY/SELL — not applicable for HOLD with no instrument).
- Current price from `price_quotes` table via `IPriceQuoteQueryService`.
- For decisions with no linked instrument (pure REBALANCE), `evaluate_decision` returns status MIXED with a note that instrument-level eval is unavailable.

Benchmark-relative: confirmed OUT OF SCOPE for v1.

### Q3 — Quantity units + price snapshot semantics

Three quantity unit modes:
- `SHARES` — raw count. `price_at_decision` is fetched per ISIN from the price feed at record time and stored as `(amount, currency)` in two columns (`price_at_decision_amount numeric(28,8)`, `price_at_decision_currency char(3)`). Standard `Money` VO pattern.
- `AMOUNT_BASE_CCY` — user says "I want to invest 5000 EUR". `price_at_decision` is still snapshotted per ISIN (so evaluation is possible); the declared amount is stored in `quantity_amount` + `quantity_currency` (also a `Money`-shaped pair). Evaluation computes the implied shares at time-of-decision and uses the current price to evaluate.
- `PERCENT_PORTFOLIO` — user says "move 15% of portfolio to X". `price_at_decision` per ISIN still snapshotted; `quantity` stores the percentage as a plain decimal (0..100). Evaluation is approximate (portfolio size at decision time is not stored in v1 — record the percentage, evaluate directionally).

**Key constraint**: `price_at_decision` is always a per-ISIN snapshot regardless of unit mode. The snapshot is captured at the moment `record_decision` is called — the MCP tool fetches the current price from the backend (via `GET /api/positions/` or a dedicated price endpoint) and stores it. If no ISIN is provided (vague HOLD/REBALANCE without instrument), the snapshot columns are NULL.

The `Money` VO is used for rendering but NOT persisted as a composite type — same split-column pattern as `Trade` (`price_amount` + `price_currency`).

### Q4 — Multi-tenancy pattern reference

Confirmed pattern from `TradeEntity` + `TradeRepository` + `TradeQueryService`:
- **Entity** (`InvestmentDecisionEntity`): carries `AccountId Guid` field.
- **Repository** (`IInvestmentDecisionRepository` → `InvestmentDecisionRepository`): ALL EF queries filter by `AccountId` in WHERE clause. FK to `accounts.id` with `ON DELETE CASCADE`.
- **Query service** (`IInvestmentDecisionQueryService` → `InvestmentDecisionQueryService`): Dapper queries parameterize `@AccountId`. After fetching, call `AccountGuard.EnforceMany(rows, accountId, d => d.AccountId)`.
- **Endpoint**: receives `AccountId` from `AccessorClassEntity accessor` (injected by `AccessorClassMiddleware` from the JWT claim).

Template file: `backend/src/Valyze.Infraestructure.QueryService/Portfolio/TradeQueryService.cs` — the `ListByAccountAsync` method is the canonical reference.

### Q5 — MCP tool surface

**4 tools — 3 required + 1 stretch promoted to v1:**

All four are low-risk (2 writes + 2 reads) and the track-record aggregate is the core value proposition.

| Tool | Signature | Notes |
|------|-----------|-------|
| `record_decision` | `(source, action, isin?, ticker?, quantity?, units?, rationale, horizon_days?)` | `source` REQUIRED. `isin` and `ticker` are both optional (a pure HOLD journal entry may name neither). `horizon_days` if null → defaults per action. Returns the created decision id. |
| `list_decisions` | `(limit?, since?, source?, action?, isin?)` | Returns most recent first. |
| `evaluate_decision` | `(id)` | Returns `{ status, returnPercent, daysElapsed, horizon, priceThen, priceNow }`. |
| `get_decision_track_record` | `(source?)` | Aggregate: total decisions, % achieved, % underperforming, % pending, avg return by source. Promoted from stretch — it's the core feedback loop. |

Pattern to follow: `backend/src/Valyze.Mcp/Tools/NewsTools.cs` — static class with `[McpServerToolType]`, methods with `[McpServerTool(Name = "...")]` and `[Description(...)]`. `ValyzeApiClient.PostJsonAsync` / `GetJsonAsync` for HTTP calls.

New file: `backend/src/Valyze.Mcp/Tools/DecisionTools.cs`.

### Q6 — Persona instruction hook

**Recommendation: BOTH tool docstring AND ServerInstructions — complementary, not redundant.**

- **Tool docstring** on `record_decision`: "IMPORTANT: before calling this tool, you MUST ask the user which source applies and confirm with them. Never infer source from context." — this is the machine-readable guardrail visible at tool selection time.
- **ServerInstructions addition** (a short bullet in the "Tool selection guide" section): "Before invoking `record_decision`, always confirm `source` with the user — never infer it." — this is the model-level instruction that applies before the tool is even considered.

The dual approach is necessary because: (a) the tool docstring is consulted at invocation time, but (b) the persona instruction shapes behavior during conversation planning before tool invocation. One without the other is weaker. Both is two lines of text.

Where to add in ServerInstructions: in the "Tool selection guide" section, under a new "Decision tracking (MCP)" subsection, parallel to the existing "Portfolio", "News", and "Web" subsections.

### Q7 — Chat session linkage (`ai_chat_session_id`)

**Confirmed: leave column NULLABLE, unpopulated in v1.**

- The column `ai_chat_session_id UUID NULL` is added to the schema in this feature's migration.
- In v1, `record_decision` does NOT populate it — the chat-persistence-DB feature (SDD #3) has not landed and the session ID is not available in the MCP tool call context.
- SDD #3 will add the session ID to the MCP context (likely via a request header or a dedicated parameter) and start populating this column at that point. No migration needed then — the column already exists as NULL.
- Document this in the migration comment.

---

## Code patterns to follow

- **Entity template**: `backend/src/Valyze.Domain/Entities/Portfolio/TradeEntity.cs` — POCO with `AccountId`, `InstrumentRef` VO, `Money` VO via split columns. Currency stored as `Currency` VO in domain, persisted as `char(3)` string in EF.
- **Repository template**: `backend/src/Valyze.Infraestructure.Repository/Portfolio/TradeRepository.cs` — EF Core, injects `ValyzeDbContext`, all queries scope by `AccountId`.
- **Query service template**: `backend/src/Valyze.Infraestructure.QueryService/Portfolio/TradeQueryService.cs` — Dapper, extends `BaseQueryService`, uses `AccountGuard.EnforceMany` post-fetch, `sealed record` for row projection.
- **EF mapping template**: `backend/src/Valyze.Infraestructure.EntityFramework/Mapper/TradeConfiguration.cs` — `HasColumnType("numeric(28, 8)")` for money amounts, `HasMaxLength(3)` for currency codes, explicit FK with `ON DELETE Cascade`, partial unique index pattern.
- **Enum persistence pattern**: `short` in EF entity (e.g. `NewsSource.Scope`), explicit cast in mapper (`(short)enum` → ToEf, `(EnumType)short` → ToDomain). Same pattern for `DecisionAction` and `DecisionSource` enums.
- **Endpoint template**: `backend/src/Valyze.Host/MinimalApi/News/NewsEndpoints.cs` — `MapGroup("/api/decisions")`, `.RequireAuthorization()`, `AccessorClassEntity accessor` injected into handlers, private `ToDto` projection methods.
- **MCP tool template**: `backend/src/Valyze.Mcp/Tools/NewsTools.cs` — `[McpServerToolType]` static class, `[McpServerTool(Name = "...")]` + `[Description(...)]` per method, `ValyzeApiClient` injected, `PostJsonAsync`/`GetJsonAsync` for HTTP.

---

## Risks / unknowns

1. **Price snapshot at record time**: `record_decision` MCP tool must call the backend to fetch the current price for the given ISIN before persisting. This requires a new thin endpoint `GET /api/market/price?symbol=X` (or reuse the positions endpoint result). If the price feed returns no quote, the decision is still saved but with `price_at_decision_amount = NULL`. Evaluation with a null snapshot must return a clear "price unavailable at decision time" rather than a divide-by-zero.

2. **Enum persistence**: `DecisionAction` and `DecisionSource` both need to be persisted as `short` in the EF entity and as string in the API response (for readability). The mapper cast pattern is established, but the domain enum values must be explicitly assigned integers so future additions don't reorder existing data.

3. **`REBALANCE` with multiple legs**: a REBALANCE might involve selling A and buying B. v1 treats it as a single decision record with a single ISIN (or none). Future: a `decision_legs` table. Document this limitation in the spec.

4. **PERCENT_PORTFOLIO evaluation gap**: storing percentage but not the portfolio value at decision time means percent-unit decisions can only be evaluated directionally (did the instrument go up/down?), not in absolute return terms. This is a known v1 limitation — document it clearly in the spec and in the MCP tool description.

5. **`linked_trade_id` ON DELETE SET NULL**: EF Core supports `ON DELETE SET NULL` but requires the FK property to be nullable and the navigation property configured correctly. The `TradeConfiguration` uses `ON DELETE Cascade` as the existing pattern — this FK needs different behavior. Verify EF migration generates the correct SQL.

6. **`get_decision_track_record` performance**: once decisions accumulate, an aggregate query across all decisions for an account could be expensive without an index. Index on `(account_id, source, status)` should be included in the migration from the start.

---

## Recommendations for the proposal phase

1. Merge `DecisionAction` and `DecisionSource` enum definitions into the schema design — they're small (4 and 5 values respectively) and must be numeric-stable.

2. The "price at decision" fetch should be a new dedicated backend endpoint `GET /api/market/price?symbol={isin}` that the MCP tool calls before `POST /api/decisions/`. This keeps the concern isolated and reusable by the future suggestion pipeline. Alternatively, reuse the existing positions endpoint (already available), but that's heavier — new endpoint is cleaner.

3. Propose the `DecisionTools.cs` MCP file and the ServerInstructions addition as a single atomic change — they're coupled by design.

4. The `openspec/changes/investment-decisions-tracker/` folder should contain:
   - Schema diagram (investment_decisions table + FK to trades + FK to accounts)
   - Enum tables (DecisionAction, DecisionSource)
   - MCP tool signatures

5. Strict TDD note (from openspec config): `strict_tdd: true`. Tests for `IRecordDecisionUseCase` and `IEvaluateDecisionUseCase` must be written before the implementation. Domain-level tests (Money invariants, evaluation logic, status transitions) are the priority.

---

## Affected areas summary

| Layer | File/Path | Change |
|-------|-----------|--------|
| Domain | `Valyze.Domain/Enum/DecisionAction.cs` (new) | New enum |
| Domain | `Valyze.Domain/Enum/DecisionSource.cs` (new) | New enum |
| Domain | `Valyze.Domain/Entities/Decisions/InvestmentDecisionEntity.cs` (new) | New entity |
| Domain | `Valyze.Domain/Application/Decisions/IDecisionUseCases.cs` (new) | Use case interfaces + commands |
| Domain | `Valyze.Domain/Repository/IInvestmentDecisionRepository.cs` (new) | Write-side port |
| Domain | `Valyze.Domain/QueryService/IInvestmentDecisionQueryService.cs` (new) | Read-side port |
| Application | `Valyze.Application/Decisions/DecisionUseCases.cs` (new) | Use case implementations |
| Application | `Valyze.Application/ServiceExtensions.cs` | Register new use cases |
| EF | `Valyze.Infraestructure.EntityFramework/Entities/InvestmentDecision.cs` (new) | EF entity |
| EF | `Valyze.Infraestructure.EntityFramework/Mapper/InvestmentDecisionConfiguration.cs` (new) | EF config |
| EF | `Valyze.Infraestructure.EntityFramework/Mapper/InvestmentDecisionMapper.cs` (new) | ToEf/ToDomain |
| EF | `Valyze.Infraestructure.EntityFramework/ValyzeDbContext.cs` | Add DbSet |
| EF | `Migrations/` | New migration |
| Repository | `Valyze.Infraestructure.Repository/Decisions/InvestmentDecisionRepository.cs` (new) | EF repo |
| Repository | `Valyze.Infraestructure.Repository/ServiceExtensions.cs` | Register |
| QueryService | `Valyze.Infraestructure.QueryService/Decisions/InvestmentDecisionQueryService.cs` (new) | Dapper |
| QueryService | `Valyze.Infraestructure.QueryService/ServiceExtensions.cs` | Register |
| Host | `Valyze.Host/MinimalApi/Decisions/DecisionEndpoints.cs` (new) | Minimal API |
| Host | `Valyze.Host/MinimalApi/MapMinimalApiExtensions.cs` | Register |
| MCP | `Valyze.Mcp/Tools/DecisionTools.cs` (new) | 4 MCP tools |
| MCP | `Valyze.Mcp/Program.cs` | ServerInstructions addition (2 lines) |
| Tauri | `tauri/src-tauri/src/claude_chat.rs` | Add 4 entries to VALYZE_MCP_TOOLS |
| Tests | `Valyze.Domain.Tests/Decisions/` (new) | TDD-first unit tests |
