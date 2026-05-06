# Tasks: investment-decisions-tracker

Test runner: `dotnet test` (from `backend/`)
Strict TDD: tests precede implementation within each subsection.

---

## Phase 1 — Spec reconciliation (preflight)

- [ ] 1.1 Update `spec.md`: replace every occurrence of `MIXED` used for HOLD-without-instrument or NULL-price scenarios with `NOT_APPLICABLE`. Add `NOT_APPLICABLE = 5` to the `DecisionStatus` enum table in spec.md.
- [ ] 1.2 Update `spec.md`: add `link_decision_to_trade` to the "Tauri MCP allowlist" requirement — change count from 4 to **5** tools, adding `mcp__valyze__link_decision_to_trade` to the required entries list.
- [ ] 1.3 Update `spec.md`: add scenarios for the new MCP tool `link_decision_to_trade` under the "Link a trade to a decision" requirement block:
  - Happy path: call with `{ decisionId, tradeId }` → `linked_trade_id` updated.
  - Clear path: call with `{ decisionId, tradeId: null }` → link cleared.
  - Cross-account negative: `decisionId` owned by acc-1, `tradeId` owned by acc-2 → rejected.
- [ ] 1.4 Update `spec.md` acceptance criteria: replace `"MIXED (instrument-level evaluation is not applicable)"` in scenario dec-6 (REBALANCE no instrument) with `NOT_APPLICABLE`.
- [ ] 1.5 Update `spec.md`: align `DecisionStatus` enum table to match design — add `NOT_APPLICABLE = 5` row; update `MIXED` description to "multi-leg REBALANCE where some legs win/some lose (v1: single-leg approximation)".

---

## Phase 2 — Domain (Valyze.Domain) — TDD

### 2.1 Enums

- [ ] 2.1.1 **Test**: `Valyze.Domain.Tests/Decisions/EnumStabilityTests.cs` — pin all numeric values for `DecisionAction` (1..4), `DecisionSource` (1..5), `QuantityUnits` (1..3), `DecisionStatus` (1..5 including `NotApplicable=5`). Fail if any value shifts.
- [ ] 2.1.2 Add `Valyze.Domain/Enum/DecisionAction.cs` — `Buy=1, Sell=2, Hold=3, Rebalance=4` typed as `short`.
- [ ] 2.1.3 Add `Valyze.Domain/Enum/DecisionSource.cs` — `AiRecommendation=1, UserOwnAnalysis=2, ExternalNews=3, ThirdPartyTip=4, Other=5` typed as `short`.
- [ ] 2.1.4 Add `Valyze.Domain/Enum/QuantityUnits.cs` — `Shares=1, AmountBaseCcy=2, PercentPortfolio=3` typed as `short`.
- [ ] 2.1.5 Add `Valyze.Domain/Enum/DecisionStatus.cs` — `PendingHorizon=1, Achieved=2, Underperforming=3, Mixed=4, NotApplicable=5` typed as `short`. Not persisted — computed at evaluation time only.

### 2.2 Entity

- [ ] 2.2.1 **Test**: `Valyze.Domain.Tests/Decisions/InvestmentDecisionEntityTests.cs`:
  - `AccountId` of `Guid.Empty` is rejected by use case validator (guard in application layer — entity stays a POCO, test documents the invariant contract).
  - `Rationale` whitespace-only is invalid (tested at use case boundary).
  - `PriceAtDecision` snapshot columns are both null OR both set — never one without the other (test a helper that validates the pair).
  - `Source = Other` allows non-null `SourceOtherNote`; all other sources must have `SourceOtherNote = null`.
- [ ] 2.2.2 Add `Valyze.Domain/Entities/Decisions/InvestmentDecisionEntity.cs` — POCO matching the design schema with all columns from the DDL. `PriceAtDecision` typed as `Money?`.

### 2.3 Value objects

- [ ] 2.3.1 **Test**: `Valyze.Domain.Tests/Decisions/DecisionEvaluationTests.cs` — construction with each `DecisionStatus` value; assert `ReturnPercent` is null for `PendingHorizon` and `NotApplicable`; assert currency mismatch between `PriceThen` and `PriceNow` would surface as error at the use case layer.
- [ ] 2.3.2 Add `Valyze.Domain/Decisions/DecisionEvaluation.cs` — sealed record matching the design: `(DecisionStatus Status, decimal? ReturnPercent, int DaysElapsed, int Horizon, Money? PriceThen, Money? PriceNow, string? Message)`.
- [ ] 2.3.3 **Test**: `Valyze.Domain.Tests/Decisions/DecisionTrackRecordTests.cs` — construction with multiple `DecisionTrackRecordRow` entries; verify `BySource` count; verify `AvgReturnPercent = null` when no resolved decisions.
- [ ] 2.3.4 Add `Valyze.Domain/Decisions/DecisionTrackRecord.cs` — sealed record `(IReadOnlyList<DecisionTrackRecordRow> BySource)`. Add `DecisionTrackRecordRow.cs` in the same folder.

### 2.4 Use case interfaces

- [ ] 2.4.1 Add `Valyze.Domain/Application/Decisions/IDecisionUseCases.cs` — single file hosting all 5 interfaces and their input/output types (matches existing co-location convention):
  - `IRecordDecisionUseCase` + `RecordDecisionCommand` + `RecordDecisionResult`.
  - `IListDecisionsUseCase` + `ListDecisionsQuery`.
  - `IEvaluateDecisionUseCase`.
  - `IGetDecisionTrackRecordUseCase`.
  - `ILinkDecisionToTradeUseCase`.

### 2.5 Repository + query service interfaces

- [ ] 2.5.1 Add `Valyze.Domain/Repository/IInvestmentDecisionRepository.cs` — 3 methods: `CreateAsync`, `UpdateLinkedTradeAsync`, `GetByIdForAccountAsync`.
- [ ] 2.5.2 Add `Valyze.Domain/QueryService/IInvestmentDecisionQueryService.cs` — 2 methods: `ListByAccountAsync`, `GetTrackRecordAsync`.

---

## Phase 3 — Application (Valyze.Application) — TDD

### 3.1 RecordDecisionUseCase

- [ ] 3.1.1 **Test**: `Valyze.Domain.Tests/Decisions/RecordDecisionUseCaseTests.cs` — happy path: BUY + `AI_RECOMMENDATION` + price feed returns 100.00 EUR → decision persisted with snapshot, `Warning = null`.
- [ ] 3.1.2 **Test**: price feed unavailable (mock `IPriceQuoteQueryService` returning null) → decision persisted with `PriceAtDecision = null`, `RecordDecisionResult.Warning` is non-null and descriptive.
- [ ] 3.1.3 **Test**: `source = Other` with `SourceOtherNote` populated → persisted correctly.
- [ ] 3.1.4 **Test**: missing `source` (invalid enum cast / zero value) rejected → `BusinessException` thrown before repository call.
- [ ] 3.1.5 **Test**: default horizon resolution — 4 parameterized cases: `Buy=180, Sell=30, Hold=90, Rebalance=90`. When caller supplies `EvaluationHorizonDays`, that value is used instead.
- [ ] 3.1.6 Implement `Valyze.Application/Decisions/RecordDecisionUseCase.cs`. Calls `IPriceQuoteQueryService.GetFreshAsync` before the `INSERT`. Applies default horizon if `command.EvaluationHorizonDays` is null. Enforces `AccountId` from command (never caller-overrideable by HTTP body — that guard sits at endpoint level, not use case level).

### 3.2 EvaluateDecisionUseCase

- [ ] 3.2.1 **Test**: `daysElapsed < horizon` → `Status = PendingHorizon`, `ReturnPercent = null`. (any price delta irrelevant)
- [ ] 3.2.2 **Test**: BUY past horizon, +6% return (threshold 0.05) → `Status = Achieved`.
- [ ] 3.2.3 **Test**: BUY past horizon, -6% return → `Status = Underperforming`.
- [ ] 3.2.4 **Test**: SELL past horizon, -6% return → `Status = Achieved` (we wanted price down).
- [ ] 3.2.5 **Test**: HOLD past horizon, instrument-less (`Isin = null`) → `Status = NotApplicable`, message "instrument-less HOLD".
- [ ] 3.2.6 **Test**: `PriceAtDecision = null` (feed was unavailable at record time) → `Status = NotApplicable`, message "price unavailable at decision time".
- [ ] 3.2.7 **Test**: REBALANCE past horizon with valid price snapshot → `Status = Mixed`, `ReturnPercent` = single-leg return percent.
- [ ] 3.2.8 **Test**: custom `AchievementThreshold = 0.10` via `IOptions` mock → +6% return on BUY is NOT `Achieved` (below threshold), +11% is `Achieved`.
- [ ] 3.2.9 **Test**: cross-account access → decision looked up with wrong `AccountId` → `BusinessException` (not found, no info leak).
- [ ] 3.2.10 Implement `Valyze.Application/Decisions/EvaluateDecisionUseCase.cs`. Inject `IOptions<DecisionEvaluationOptions>` and `IPriceQuoteQueryService`. Follow evaluation pseudo-code from design exactly.

### 3.3 ListDecisionsUseCase

- [ ] 3.3.1 **Test**: list with each filter in isolation (`limit`, `since`, `source`, `action`, `isin`) — mock query service, verify correct `ListDecisionsQuery` forwarded.
- [ ] 3.3.2 **Test**: cross-account isolation — `AccountId` in query comes from `command.AccountId`, never from request body override; mock verifies correct `AccountId` forwarded to query service.
- [ ] 3.3.3 Implement `Valyze.Application/Decisions/ListDecisionsUseCase.cs`.

### 3.4 GetDecisionTrackRecordUseCase

- [ ] 3.4.1 **Test**: aggregation by source — mock query service returns known rows, verify `DecisionTrackRecord.BySource` count and per-row values.
- [ ] 3.4.2 **Test**: `sourceFilter` parameter forwarded correctly to query service.
- [ ] 3.4.3 **Test**: empty account (query service returns empty list) → `DecisionTrackRecord` with empty `BySource`, no exception.
- [ ] 3.4.4 Implement `Valyze.Application/Decisions/GetDecisionTrackRecordUseCase.cs`. Reads `IOptions<DecisionEvaluationOptions>` and passes `AchievementThreshold` to query service.

### 3.5 LinkDecisionToTradeUseCase

- [ ] 3.5.1 **Test**: link decision to trade in same account → `IInvestmentDecisionRepository.UpdateLinkedTradeAsync` called with correct args.
- [ ] 3.5.2 **Test**: `tradeId = null` clears link → `UpdateLinkedTradeAsync` called with `null` trade id.
- [ ] 3.5.3 **Test**: cross-account rejection — decision in acc-1 with trade belonging to acc-2 → `BusinessException` (repo throws, use case propagates).
- [ ] 3.5.4 **Test**: trade not found → `BusinessException` propagated as 404 at endpoint layer.
- [ ] 3.5.5 Implement `Valyze.Application/Decisions/LinkDecisionToTradeUseCase.cs`.

### 3.6 DI registration

- [ ] 3.6.1 Update `Valyze.Application/ServiceExtensions.cs`: register all 5 use cases as `Scoped`. Add `services.Configure<DecisionEvaluationOptions>(configuration.GetSection("Decisions:Evaluation"))` here (Application owns this options binding).

---

## Phase 4 — Configuration & Options

- [ ] 4.1 Add `Valyze.Application/Decisions/DecisionEvaluationOptions.cs` — `public decimal AchievementThreshold { get; set; } = 0.05m;`.
- [ ] 4.2 **Test**: `Valyze.Domain.Tests/Decisions/DecisionEvaluationOptionsTests.cs` — default value is `0.05m`; override to `0.10m` via `IOptions` mock works. (Covered by 3.2.8 — verify no gap; add dedicated test only if 3.2.8 doesn't exercise options binding directly.)
- [ ] 4.3 Update `backend/src/Valyze.Host/appsettings.json` — add `"Decisions": { "Evaluation": { "AchievementThreshold": 0.05 } }`.

---

## Phase 5 — Infraestructure: EntityFramework

### 5.1 EF entity + configuration

- [ ] 5.1.1 Add `Valyze.Infraestructure.EntityFramework/Entities/InvestmentDecision.cs` — EF POCO (no suffix, internal class). Split `Money?` into two scalar columns (`price_at_decision_amount numeric(28,8)`, `price_at_decision_currency char(3)`). Include `linked_trade_id uuid NULL`, `ai_chat_session_id uuid NULL`, and all other columns from the DDL.
- [ ] 5.1.2 Add `Valyze.Infraestructure.EntityFramework/Mapper/InvestmentDecisionConfiguration.cs` — `IEntityTypeConfiguration<InvestmentDecision>`. Configure:
  - `numeric(28,8)` for amount columns, `char(3)` for currency columns.
  - `source` / `action` / `quantity_units` persisted as `short` (explicit cast).
  - `account_id` FK: `ON DELETE CASCADE`.
  - `linked_trade_id` FK: `ON DELETE SET NULL` (`HasOne<Trade>().WithMany().HasForeignKey(...).IsRequired(false).OnDelete(DeleteBehavior.SetNull)`).
  - `ai_chat_session_id` column comment: `-- Populated by SDD #3 (chat-persistence-DB). NULL in v1.`
  - All 4 indexes from the design DDL.
- [ ] 5.1.3 Add `Valyze.Infraestructure.EntityFramework/Mapper/InvestmentDecisionMapper.cs` — static class with `ToEf(InvestmentDecisionEntity) → InvestmentDecision` and `ToDomain(InvestmentDecision) → InvestmentDecisionEntity`. Split/merge `Money?` in both directions.
- [ ] 5.1.4 **Test**: `Valyze.Domain.Tests/Decisions/InvestmentDecisionMapperTests.cs` — round-trip mapper test: build entity → `ToEf()` → `ToDomain()` → assert all fields preserved. Cover: `PriceAtDecision = null` case, `PriceAtDecision` with value, `LinkedTradeId = null`, `SourceOtherNote`.
- [ ] 5.1.5 Update `Valyze.Infraestructure.EntityFramework/ValyzeDbContext.cs` — add `DbSet<InvestmentDecision> InvestmentDecisions` and `modelBuilder.ApplyConfiguration(new InvestmentDecisionConfiguration())` in `OnModelCreating`.

### 5.2 Migration

- [ ] 5.2.1 Run: `dotnet ef migrations add AddInvestmentDecisionsTable --project src/Valyze.Infraestructure.EntityFramework --startup-project src/Valyze.Host --output-dir Migrations` (from `backend/`). **Note**: close any active Tauri chat session first — `valyze-mcp.exe` may lock the build output.
- [ ] 5.2.2 Inspect the generated `*.cs` migration file: confirm `linked_trade_id` FK uses `onDelete: ReferentialAction.SetNull`. If it shows `Restrict` or `Cascade`, fix `InvestmentDecisionConfiguration` and regenerate.
- [ ] 5.2.3 Confirm the generated migration includes all 4 indexes with the exact names from the design: `ix_investment_decisions_account_created`, `ix_investment_decisions_account_source_action_created`, `ix_investment_decisions_account_isin` (with `WHERE isin IS NOT NULL`), `ix_investment_decisions_linked_trade_id` (with `WHERE linked_trade_id IS NOT NULL`).
- [ ] 5.2.4 Confirm `Down()` drops all 4 indexes and the table. Migration is reversible.
- [ ] 5.2.5 Apply migration in dev: `dotnet ef database update --project src/Valyze.Infraestructure.EntityFramework --startup-project src/Valyze.Host`.

---

## Phase 6 — Infraestructure: Repository

- [ ] 6.1 Add `Valyze.Infraestructure.Repository/Decisions/InvestmentDecisionRepository.cs` — EF Core, scoped, implements `IInvestmentDecisionRepository`:
  - `CreateAsync`: maps entity to EF POCO via mapper, `Add`, `SaveChangesAsync`, returns `Guid`.
  - `UpdateLinkedTradeAsync`: fetch by `(id, accountId)`, throw `BusinessException` if not found, set `LinkedTradeId`, `SaveChangesAsync`.
  - `GetByIdForAccountAsync`: `FirstOrDefaultAsync` filtered by `(id, accountId)`, map to domain entity.
- [ ] 6.2 Update `Valyze.Infraestructure.Repository/ServiceExtensions.cs` — add `services.AddScoped<IInvestmentDecisionRepository, InvestmentDecisionRepository>()`.

---

## Phase 7 — Infraestructure: QueryService

- [ ] 7.1 Add `Valyze.Infraestructure.QueryService/Decisions/InvestmentDecisionQueryService.cs` — extends `BaseQueryService`, Dapper, parameterized `@AccountId`:
  - `ListByAccountAsync`: builds dynamic SQL from `ListDecisionsQuery` filters. Results post-validated with `AccountGuard.EnforceMany`.
  - `GetTrackRecordAsync`: executes the track-record aggregation SQL from the design (CTE `latest_quote` + grouped `CASE` expressions). Passes `@AccountId`, `@SourceFilter`, `@Threshold` as Dapper params. Post-validates with `AccountGuard.EnforceMany`.
- [ ] 7.2 **Test**: `Valyze.Domain.Tests/Decisions/TrackRecordAggregationRegressionTests.cs` — seed known data with a mock query service that returns deterministic rows; assert `GetDecisionTrackRecordUseCase` output matches the expected per-source counts and percentages. This is the regression guard for drift between SQL `CASE` logic and the use case evaluation logic.
- [ ] 7.3 Update `Valyze.Infraestructure.QueryService/ServiceExtensions.cs` — add `services.AddScoped<IInvestmentDecisionQueryService, InvestmentDecisionQueryService>()`.

---

## Phase 8 — Infraestructure: MarketData (price endpoint support)

- [ ] 8.1 Inspect `IPriceQuoteQueryService` in `Valyze.Domain/QueryService/` — verify it exposes a `GetFreshAsync(IEnumerable<string> isins, Currency currency, DateTimeOffset freshnessCutoff, CancellationToken ct)` method (or equivalent single-symbol latest-price lookup). If missing, add the method signature to the interface and implement in the existing `*QueryService` under `Valyze.Infraestructure.QueryService/`.
- [ ] 8.2 **Test**: `Valyze.Domain.Tests/MarketData/PriceQuoteQueryServiceTests.cs` (or extend existing) — mock returns `Money(100.00, EUR)` when quote exists; returns `null` when no quote in `price_quotes` within freshness window.

---

## Phase 9 — Host: API endpoints

### 9.1 Decisions endpoints

- [ ] 9.1.1 Add `Valyze.Host/MinimalApi/Decisions/DecisionEndpoints.cs` — 5 handlers:
  - `POST /api/decisions` → `IRecordDecisionUseCase`. Extracts `AccountId` from `AccessorClassEntity` (ignores any `accountId` in request body). Returns `201 { id, warning? }`. Returns `422` with error description if `source` is missing/invalid or `action` is missing.
  - `GET /api/decisions` → `IListDecisionsUseCase`. Binds `?limit&since&source&action&isin`. Returns `200 { count, decisions: [...] }`.
  - `GET /api/decisions/{id}/evaluate` → `IEvaluateDecisionUseCase`. Returns `200 { status, returnPercent?, daysElapsed, horizon, priceThen?, priceNow?, message? }`. Maps `BusinessException` not-found to `404`.
  - `GET /api/decisions/track-record` → `IGetDecisionTrackRecordUseCase`. Returns `200 { bySource: [...] }`.
  - `PATCH /api/decisions/{id}/link-trade` → `ILinkDecisionToTradeUseCase`. Body `{ tradeId: uuid | null }`. Returns `204 No Content`. Maps `BusinessException` not-found to `404`.
- [ ] 9.1.2 Add `Map{Feature}Endpoints` static extension method on `DecisionEndpoints`. Group: `MapGroup("/api/decisions").RequireAuthorization()`. `AccessorClassEntity` injected per handler.
- [ ] 9.1.3 Update `Valyze.Host/MinimalApi/MapMinimalApiExtensions.cs` — call `app.MapDecisionEndpoints()`.

### 9.2 Market price endpoint

- [ ] 9.2.1 Add `Valyze.Host/MinimalApi/Market/MarketEndpoints.cs` — `GET /api/market/price?symbol={isin}`. Backed by `IPriceQuoteQueryService.GetFreshAsync` with 24h freshness window. Returns `200 { amount, currency, ts }` when found; `404 { reason: "no fresh quote" }` when not. Requires JWT (`RequireAuthorization()`). Rejects unauthenticated callers with `401`.
- [ ] 9.2.2 Update `Valyze.Host/MinimalApi/MapMinimalApiExtensions.cs` — call `app.MapMarketEndpoints()`.

---

## Phase 10 — MCP server (Valyze.Mcp)

- [ ] 10.1 Add `Valyze.Mcp/Tools/DecisionTools.cs` — `[McpServerToolType]` static class with **5** tools (pattern matches `NewsTools.cs`):
  - `record_decision` — with source-confirmation guardrail in `[Description]` (exact wording from design).
  - `list_decisions`.
  - `evaluate_decision`.
  - `get_decision_track_record`.
  - `link_decision_to_trade(decisionId: string, tradeId: string?)` → calls `PATCH /api/decisions/{id}/link-trade` with `{ tradeId }`. `tradeId = null` clears the link.
- [ ] 10.2 Update `Valyze.Mcp/Program.cs` `ServerInstructions`:
  - Insert "Decision tracking (MCP):" subsection immediately after the "News (MCP)" subsection and before "Web", exactly as specified in the design.
  - Include the post-PDF-import nudge bullet: "After a PDF import surfaces a new trade, ask ONCE if it matches an open unlinked decision (same ISIN, recent date) → on user confirmation call `link_decision_to_trade`. Don't auto-link."
  - Ensure the source-confirmation instruction is present: "BEFORE calling `record_decision`, ALWAYS confirm `source` with the user — never infer it from context."
- [ ] 10.3 Build MCP: `dotnet build src/Valyze.Mcp/Valyze.Mcp.csproj` (from `backend/`). **IMPORTANT**: close any open Tauri chat window first — `valyze-mcp.exe` is locked while a chat session is active, and the build will fail silently or with a file-in-use error.

---

## Phase 11 — Tauri shell

- [ ] 11.1 Update `tauri/src-tauri/src/claude_chat.rs` `VALYZE_MCP_TOOLS` array — add **5** entries after the News block:
  ```rust
  // Decisions (record + read + evaluate + track-record + link)
  "mcp__valyze__record_decision",
  "mcp__valyze__list_decisions",
  "mcp__valyze__evaluate_decision",
  "mcp__valyze__get_decision_track_record",
  "mcp__valyze__link_decision_to_trade",
  ```
  **Note**: after editing this file, `cargo build` is required before the new tools are accessible in a live chat session. Follow the launch-valyze skill (free port 1420 first).

---

## Phase 12 — End-to-end manual smoke

- [ ] 12.1 Restart backend: `dotnet run --project src/Valyze.Host` (from `backend/`).
- [ ] 12.2 Restart Tauri app per launch-valyze skill (free port 1420 first if a prior instance is running).
- [ ] 12.3 Open a fresh chat. Tell the AI "voy a comprar 5 AAPL siguiendo tu sugerencia". Verify:
  - AI asks which `source` applies BEFORE calling `record_decision` — it must NOT infer `AI_RECOMMENDATION` automatically.
  - After user confirms source, `record_decision` is invoked with that `source`.
  - Response includes a decision `id` (UUID).
  - If price feed has no quote for AAPL: response includes a `warning` and `record_decision` surfaces it to the user.
- [ ] 12.4 Call `list_decisions` (or ask "mostrá mis decisiones"). Verify the new decision appears first.
- [ ] 12.5 Call `evaluate_decision` with the returned id. Verify `status = PENDING_HORIZON` and `returnPercent = null` (decision just created, well within horizon).
- [ ] 12.6 Call `get_decision_track_record`. Verify aggregation shows 1 decision under the source the user confirmed; `achieved_pct` is 0 (all pending).
- [ ] 12.7 To test status flip: directly set `created_at` to `NOW() - INTERVAL '200 days'` for the decision row in Postgres. Re-call `evaluate_decision`. Verify status changes to `Achieved` or `Underperforming` depending on current price vs snapshot price.
- [ ] 12.8 Test `link_decision_to_trade`: ask AI to link the decision to a trade (use a known trade id from `GET /api/trades`). Verify `linked_trade_id` updated. Then call with `tradeId: null`. Verify link cleared.
- [ ] 12.9 Multi-tenancy assertion: either a repository unit test (Phase 3/6) or a direct DB query verifying the decision row carries `account_id` from the authenticated session, not a caller-supplied value.

---

## Phase 13 — Documentation

- [ ] 13.1 Update `CLAUDE.md` — "Domain Rules" section:
  - Add rule 8: "Investment decisions are the user's investment intent journal. `InvestmentDecisionEntity` is the record; `DecisionEvaluation` is always computed live from `price_quotes`, never stored. `DecisionStatus` is NOT persisted."
- [ ] 13.2 Update `CLAUDE.md` — AI Layer MCP tools table: add the 5 new decision tools with their purpose descriptions, matching the format of the existing News tools table.
- [ ] 13.3 Update `CLAUDE.md` — Persistence schema spine: add `investment_decisions` row documenting its key columns and FK behavior.
- [ ] 13.4 Verify Conventional Commits scope: use `domain` for Phase 2, `app` for Phase 3–4, `db` for Phase 5, `host` for Phases 6–9, `mcp` for Phase 10, `tauri` for Phase 11, `docs` for Phase 13.

---

## Phase 14 — Verify

- [ ] 14.1 `dotnet build` (from `backend/`) — must be clean with zero errors or warnings.
- [ ] 14.2 `dotnet test` (from `backend/`) — all new tests green; no existing tests regressed.
- [ ] 14.3 Run `/sdd-verify investment-decisions-tracker` — orchestrator validates implementation against spec.md (as updated in Phase 1).

---

## Sequencing and parallelism notes

**Sequential blockers** (each depends on the prior):
- Phase 1 → all other phases (spec must be authoritative before code references it)
- Phase 2 (Domain) → Phase 3 (Application) → Phase 5 (EF) / Phase 6 (Repository) / Phase 7 (QueryService)
- Phase 5 migration (5.2.1) → must run AFTER 5.1 entity + configuration are complete
- Phase 9 (endpoints) → Phase 10 (MCP) — MCP calls the HTTP endpoints
- Phase 10 (MCP build) → Phase 11 (Tauri allowlist) → Phase 12 (smoke test)

**Can run in parallel once dependencies satisfied:**
- Phase 2.1 enums, 2.2 entity, 2.3 VOs can all be written in parallel
- Phase 5, Phase 6, Phase 7 can be written in parallel after Phase 2–3 Domain contracts are stable
- Phase 4 (options) can be written in parallel with Phase 3
- Phase 8 (price endpoint verification) can be done independently as a spike before Phase 9
- Phase 13 (docs) can be done in parallel with Phase 12 (smoke)
