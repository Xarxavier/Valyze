# Spec: investment-decisions-tracker

## Capability
investment-decisions

---

## Context

This spec describes the observable behaviour that MUST be true after the change is applied.
It does not prescribe HOW to implement it — that is the design phase's remit.

All money values in scenarios state currency explicitly.
RFC 2119 keywords (MUST, SHALL, SHOULD, MAY) are authoritative.

---

## Domain types introduced

### Enum: `DecisionAction`
| Name | Numeric value |
|------|--------------|
| BUY | 1 |
| SELL | 2 |
| HOLD | 3 |
| REBALANCE | 4 |

### Enum: `DecisionSource`
| Name | Numeric value |
|------|--------------|
| AI_RECOMMENDATION | 1 |
| USER_OWN_ANALYSIS | 2 |
| EXTERNAL_NEWS | 3 |
| THIRD_PARTY_TIP | 4 |
| OTHER | 5 |

### Enum: `DecisionStatus`
| Name | Numeric value | Meaning |
|------|--------------|---------|
| PENDING_HORIZON | 1 | Horizon period not yet elapsed |
| ACHIEVED | 2 | Price moved favorably beyond the ±5% threshold |
| UNDERPERFORMING | 3 | Price moved unfavorably beyond the ±5% threshold |
| MIXED | 4 | multi-leg REBALANCE where some legs win/some lose (v1: single-leg approximation) |
| NOT_APPLICABLE | 5 | Instrument-less HOLD, or price snapshot unavailable at decision time |

### Enum: `QuantityUnit`
| Name | Meaning |
|------|---------|
| SHARES | Raw share/unit count |
| AMOUNT_BASE_CCY | Monetary amount in user-specified currency |
| PERCENT_PORTFOLIO | Percentage of total portfolio (0–100) |

### Default horizons (days)
| Action | Default horizon |
|--------|----------------|
| BUY | 180 |
| SELL | 30 |
| HOLD | 90 |
| REBALANCE | 90 |

The ±5% achieved/underperforming threshold is configurable and MUST be read from `IOptions<DecisionEvaluationOptions>` at evaluation time.

---

## ADDED requirements

---

### Requirement: Record an investment decision — happy path

The system MUST accept a `POST /api/decisions` request from an authenticated user and persist an `InvestmentDecisionEntity` row scoped to that user's account.

The `source` field is REQUIRED. A request missing `source` MUST be rejected with HTTP 422.

The `action` field is REQUIRED. A request missing `action` MUST be rejected with HTTP 422.

If `horizon_days` is not supplied, the system SHALL apply the default horizon for the given `action`.

#### Scenario: Record a BUY decision — SHARES unit with source AI_RECOMMENDATION
Given account "acc-1" is authenticated
And the price feed has a current quote for ISIN "IE00B4L5Y983": 100.00 EUR
When `POST /api/decisions` is called with `{ source: "AI_RECOMMENDATION", action: "BUY", isin: "IE00B4L5Y983", quantity_amount: 10, units: "SHARES", rationale: "AI suggested this ETF" }`
Then the system SHALL persist a decision row with:
  - `account_id = "acc-1"`
  - `source = AI_RECOMMENDATION`
  - `action = BUY`
  - `isin = "IE00B4L5Y983"`
  - `quantity_amount = 10`, `units = SHARES`
  - `price_at_decision_amount = 100.00`, `price_at_decision_currency = "EUR"`
  - `horizon_days = 180` (default for BUY)
  - `status = PENDING_HORIZON`
  - `linked_trade_id = NULL`
  - `ai_chat_session_id = NULL`
And the response SHALL include the generated decision `id` (UUID)
And the response HTTP status SHALL be 201

#### Scenario: Record a SELL decision — AMOUNT_BASE_CCY unit with source USER_OWN_ANALYSIS
Given account "acc-1" is authenticated
And the price feed has a current quote for ISIN "US0378331005": 175.50 USD
When `POST /api/decisions` is called with `{ source: "USER_OWN_ANALYSIS", action: "SELL", isin: "US0378331005", quantity_amount: 500, quantity_currency: "EUR", units: "AMOUNT_BASE_CCY", rationale: "Rebalancing", horizon_days: 60 }`
Then the system SHALL persist a decision row with:
  - `source = USER_OWN_ANALYSIS`
  - `action = SELL`
  - `price_at_decision_amount = 175.50`, `price_at_decision_currency = "USD"`
  - `quantity_amount = 500`, `quantity_currency = "EUR"`, `units = AMOUNT_BASE_CCY`
  - `horizon_days = 60` (caller-supplied, overrides default)
  - `status = PENDING_HORIZON`
And the response HTTP status SHALL be 201

#### Scenario: Record a HOLD decision — PERCENT_PORTFOLIO unit with source EXTERNAL_NEWS
Given account "acc-1" is authenticated
And the price feed has a current quote for ISIN "DE0005140008": 12.30 EUR
When `POST /api/decisions` is called with `{ source: "EXTERNAL_NEWS", action: "HOLD", isin: "DE0005140008", quantity_amount: 15, units: "PERCENT_PORTFOLIO", rationale: "Article suggested holding" }`
Then the system SHALL persist a decision row with:
  - `units = PERCENT_PORTFOLIO`
  - `quantity_amount = 15` (the percentage value)
  - `quantity_currency = NULL` (not applicable for PERCENT_PORTFOLIO)
  - `price_at_decision_amount = 12.30`, `price_at_decision_currency = "EUR"`
  - `horizon_days = 90` (default for HOLD)

#### Scenario: Record a REBALANCE decision — no instrument with source THIRD_PARTY_TIP
Given account "acc-1" is authenticated
When `POST /api/decisions` is called with `{ source: "THIRD_PARTY_TIP", action: "REBALANCE", rationale: "Advisor said move to bonds" }`
Then the system SHALL persist a decision row with:
  - `isin = NULL`
  - `price_at_decision_amount = NULL`
  - `price_at_decision_currency = NULL`
  - `status = PENDING_HORIZON`
  - `horizon_days = 90` (default for REBALANCE)
And the response HTTP status SHALL be 201

#### Scenario: Record a decision with source OTHER
Given account "acc-1" is authenticated
When `POST /api/decisions` is called with `{ source: "OTHER", action: "BUY", isin: "US5949181045", quantity_amount: 5, units: "SHARES", rationale: "General gut feel" }`
Then the system SHALL persist a decision row with `source = OTHER`
And the response HTTP status SHALL be 201

---

### Requirement: `source` is required — server-side validation

The system MUST reject any `POST /api/decisions` request where `source` is absent or not a valid `DecisionSource` value, regardless of what the MCP persona instruction says. This is a server-side invariant, not a UI or model-layer concern.

#### Scenario: POST without `source` is rejected
Given account "acc-1" is authenticated
When `POST /api/decisions` is called with `{ action: "BUY", isin: "US0378331005", quantity_amount: 10, units: "SHARES", rationale: "Missing source" }`
Then the response HTTP status SHALL be 422
And the response body SHALL contain an error describing the missing `source` field

#### Scenario: POST with invalid `source` value is rejected
Given account "acc-1" is authenticated
When `POST /api/decisions` is called with `{ source: "MAGIC_ORACLE", action: "BUY", isin: "US0378331005", quantity_amount: 10, units: "SHARES", rationale: "Bad source" }`
Then the response HTTP status SHALL be 422

#### Scenario: MCP tool `record_decision` called without `source` is rejected server-side
Given account "acc-1" is authenticated via the MCP tool layer
When the MCP tool `record_decision` is invoked without the `source` parameter
Then the backend `POST /api/decisions` call SHALL return HTTP 422
And the MCP tool SHALL surface a validation error to the AI, not a silent failure

---

### Requirement: Price snapshot at decision time

When `POST /api/decisions` is called with a valid ISIN, the system MUST attempt to snapshot the current price via `GET /api/market/price?symbol={isin}` BEFORE persisting the decision. If the price endpoint returns no quote, the decision MUST still be persisted with NULL snapshot columns — the write MUST NOT be blocked by a price-feed failure.

#### Scenario: Price feed available — snapshot is stored
Given account "acc-1" is authenticated
And the price feed has a current quote for ISIN "IE00B4L5Y983": 100.00 EUR as of now
When `POST /api/decisions` is called with `{ source: "USER_OWN_ANALYSIS", action: "BUY", isin: "IE00B4L5Y983", quantity_amount: 10, units: "SHARES", rationale: "Long-term hold" }`
Then `price_at_decision_amount = 100.00` and `price_at_decision_currency = "EUR"` SHALL be stored in the row

#### Scenario: Price feed unavailable — decision still saved, snapshot NULL
Given account "acc-1" is authenticated
And the price feed returns no quote for ISIN "XX1234567890"
When `POST /api/decisions` is called with `{ source: "USER_OWN_ANALYSIS", action: "BUY", isin: "XX1234567890", quantity_amount: 5, units: "SHARES", rationale: "Speculative" }`
Then the system SHALL persist the decision row with `price_at_decision_amount = NULL` and `price_at_decision_currency = NULL`
And the response HTTP status SHALL be 201
And the response body SHALL include a `warning` field indicating that price snapshot was unavailable

#### Scenario: `GET /api/market/price` endpoint — returns current price for a valid ISIN
Given account "acc-1" is authenticated
And the `price_quotes` table has a recent quote for ISIN "IE00B4L5Y983": `{ amount: 100.00, currency: "EUR", ts: <recent timestamp> }`
When `GET /api/market/price?symbol=IE00B4L5Y983` is called
Then the response HTTP status SHALL be 200
And the response body SHALL be `{ amount: 100.00, currency: "EUR", ts: <timestamp> }`

#### Scenario: `GET /api/market/price` endpoint — returns 404 when no quote exists
Given account "acc-1" is authenticated
And the `price_quotes` table has no entry for ISIN "XX0000000000"
When `GET /api/market/price?symbol=XX0000000000` is called
Then the response HTTP status SHALL be 404

---

### Requirement: Multi-tenant isolation — write path

Every decision row MUST be scoped to a single `account_id`. The repository MUST filter every EF Core query by `AccountId`. An authenticated user MUST NOT be able to create a decision on behalf of another account.

#### Scenario: Decision is stored under the authenticated account's ID
Given accounts "acc-1" and "acc-2" both exist
And account "acc-1" is the authenticated caller
When `POST /api/decisions` is called with a valid payload
Then the persisted row SHALL have `account_id = "acc-1"`
And account "acc-2" SHALL have zero new decision rows

#### Scenario: Authenticated user cannot POST a decision for another account
Given account "acc-1" is authenticated
When `POST /api/decisions` is called with `account_id: "acc-2"` explicitly in the payload
Then the system SHALL ignore the caller-supplied `account_id` and use the JWT-derived account ID ("acc-1") instead
And the persisted row SHALL have `account_id = "acc-1"`

---

### Requirement: Multi-tenant isolation — read path (list decisions)

`GET /api/decisions` MUST only return decisions belonging to the authenticated account. The query service MUST use `AccountGuard.EnforceMany` as a post-fetch guard in addition to the parameterized `@AccountId` filter.

#### Scenario: Account sees only its own decisions
Given account "acc-1" has 3 decisions
And account "acc-2" has 2 decisions
When account "acc-1" calls `GET /api/decisions`
Then the response SHALL contain exactly 3 decisions
And none of the returned decisions SHALL have `account_id = "acc-2"`

#### Scenario: Cross-account read is blocked — negative test
Given account "acc-2" has decision id "dec-99"
When account "acc-1" calls `GET /api/decisions/{dec-99}`
Then the response HTTP status SHALL be 404
And the response SHALL NOT expose any data belonging to account "acc-2"

---

### Requirement: Multi-tenant isolation — track record path

`GET /api/decisions/track-record` MUST only aggregate decisions belonging to the authenticated account.

#### Scenario: Track record never crosses account boundaries
Given account "acc-1" has 5 AI_RECOMMENDATION decisions, all ACHIEVED
And account "acc-2" has 10 AI_RECOMMENDATION decisions, all UNDERPERFORMING
When account "acc-1" calls `GET /api/decisions/track-record`
Then the response SHALL reflect only account "acc-1"'s 5 decisions
And the achieved count SHALL be 5, underperforming 0
And account "acc-2"'s data SHALL NOT appear

---

### Requirement: Evaluate a decision on demand

`GET /api/decisions/{id}/evaluation` MUST compute the return live using the stored `price_at_decision` and the current price from `IPriceQuoteQueryService`. The horizon gate MUST be honored: while `daysElapsed < horizon_days`, the status SHALL be `PENDING_HORIZON` and `return_percent` SHALL be null.

#### Scenario: Decision under horizon — returns PENDING_HORIZON, no return computed
Given account "acc-1" has decision "dec-1" with:
  - `action = BUY`, `isin = "IE00B4L5Y983"`, `price_at_decision_amount = 100.00 EUR`
  - `horizon_days = 180`, created 30 days ago
When `GET /api/decisions/dec-1/evaluation` is called
Then the response SHALL be:
  `{ status: "PENDING_HORIZON", return_percent: null, days_elapsed: 30, horizon: 180, price_then: 100.00, price_now: <current>, currency: "EUR" }`
And the response HTTP status SHALL be 200

#### Scenario: Decision past horizon, BUY, price up > 5% — ACHIEVED
Given account "acc-1" has decision "dec-2" with:
  - `action = BUY`, `isin = "IE00B4L5Y983"`, `price_at_decision_amount = 100.00 EUR`
  - `horizon_days = 180`, created 200 days ago
And the current price for "IE00B4L5Y983" is 110.00 EUR
When `GET /api/decisions/dec-2/evaluation` is called
Then `return_percent = 10.0`
And `status = "ACHIEVED"`
And `days_elapsed >= 180`

#### Scenario: Decision past horizon, BUY, price down > 5% — UNDERPERFORMING
Given account "acc-1" has decision "dec-3" with:
  - `action = BUY`, `isin = "IE00B4L5Y983"`, `price_at_decision_amount = 100.00 EUR`
  - `horizon_days = 180`, created 200 days ago
And the current price for "IE00B4L5Y983" is 88.00 EUR
When `GET /api/decisions/dec-3/evaluation` is called
Then `return_percent = -12.0`
And `status = "UNDERPERFORMING"`

#### Scenario: Decision past horizon, price moved within ±5% threshold — ACHIEVED or UNDERPERFORMING
Given account "acc-1" has decision "dec-4" with:
  - `action = BUY`, `price_at_decision_amount = 100.00 EUR`, `horizon_days = 180`, created 200 days ago
And the current price is 103.00 EUR (return = +3%, within threshold)
When `GET /api/decisions/dec-4/evaluation` is called
Then `status = "ACHIEVED"` (price went up regardless of threshold direction — threshold determines UNDERPERFORMING, not ACHIEVED for positive moves)

Note: the threshold gates UNDERPERFORMING (loss exceeds -5%). Any positive return beyond horizon is ACHIEVED regardless of magnitude. Negative return within 0..–5% is also ACHIEVED (thesis not invalidated). Negative return beyond –5% is UNDERPERFORMING. Design phase SHALL clarify the exact symmetric application.

#### Scenario: Decision with NULL price snapshot — evaluate returns NOT_APPLICABLE, not divide-by-zero
Given account "acc-1" has decision "dec-5" with `price_at_decision_amount = NULL` (price feed was unavailable at record time)
When `GET /api/decisions/dec-5/evaluation` is called
Then the response SHALL contain `{ status: "NOT_APPLICABLE", return_percent: null, note: "price unavailable at decision time" }` OR equivalent structured error
And the response HTTP status SHALL be 200 (not 500)

#### Scenario: REBALANCE with no instrument — evaluation returns NOT_APPLICABLE
Given account "acc-1" has decision "dec-6" with:
  - `action = REBALANCE`, `isin = NULL`, `horizon_days = 90`, created 100 days ago
When `GET /api/decisions/dec-6/evaluation` is called
Then `return_percent = null`
And `status = "NOT_APPLICABLE"` (instrument-level evaluation is not applicable for instrument-less REBALANCE)
And the response SHALL include a note that instrument-level evaluation is unavailable

---

### Requirement: PERCENT_PORTFOLIO decisions — directional-only evaluation (known v1 limitation)

When a decision is recorded with `units = PERCENT_PORTFOLIO`, the portfolio value at decision time is NOT stored. Therefore the system MUST evaluate such decisions directionally only (did the instrument price go up or down?) and MUST NOT attempt to compute the absolute monetary return.

#### Scenario: PERCENT_PORTFOLIO decision past horizon — directional evaluation only
Given account "acc-1" has decision "dec-7" with:
  - `action = BUY`, `units = PERCENT_PORTFOLIO`, `quantity_amount = 15`
  - `isin = "DE0005140008"`, `price_at_decision_amount = 12.30 EUR`
  - `horizon_days = 180`, created 200 days ago
And the current price for "DE0005140008" is 14.00 EUR
When `GET /api/decisions/dec-7/evaluation` is called
Then `status = "ACHIEVED"` (price went up)
And `return_percent` SHALL be computed from the ISIN price movement (not the portfolio-percent impact)
And the response SHOULD include a note indicating that the portfolio-percentage impact cannot be computed in v1

---

### Requirement: Aggregate track record by source

`GET /api/decisions/track-record` MUST return aggregated hit-rate statistics grouped by source for the authenticated account. An optional `source` query parameter narrows the result to a single source.

#### Scenario: Track record with results from multiple sources
Given account "acc-1" has:
  - 4 AI_RECOMMENDATION decisions: 3 ACHIEVED, 1 UNDERPERFORMING
  - 2 USER_OWN_ANALYSIS decisions: 1 ACHIEVED, 1 PENDING_HORIZON
When account "acc-1" calls `GET /api/decisions/track-record`
Then the response SHALL include one entry per source that has at least one decision:
  - AI_RECOMMENDATION: `{ total: 4, achieved: 3, underperforming: 1, pending: 0, achieved_pct: 75.0 }`
  - USER_OWN_ANALYSIS: `{ total: 2, achieved: 1, underperforming: 0, pending: 1, achieved_pct: 50.0 }`
And no entries from other accounts SHALL appear

#### Scenario: Track record filtered by source
Given account "acc-1" has decisions across multiple sources
When account "acc-1" calls `GET /api/decisions/track-record?source=AI_RECOMMENDATION`
Then the response SHALL contain only the AI_RECOMMENDATION aggregate
And the response HTTP status SHALL be 200

#### Scenario: Track record with no decisions returns empty result, not 404
Given account "acc-1" has no decisions recorded
When account "acc-1" calls `GET /api/decisions/track-record`
Then the response HTTP status SHALL be 200
And the response body SHALL be an empty array or empty aggregate object

---

### Requirement: Link a trade to a decision — manual flow

`PATCH /api/decisions/{id}/link-trade` MUST allow the authenticated owner of decision `{id}` to set `linked_trade_id` to a specific trade UUID. The trade MUST belong to the same account. Linking is reversible (user can link to a different trade by calling PATCH again). An authenticated user MUST NOT be able to link a trade from another account to any decision.

#### Scenario: Link a trade that belongs to the same account
Given account "acc-1" owns decision "dec-1" and trade "trade-1"
When `PATCH /api/decisions/dec-1/link-trade` is called with `{ trade_id: "trade-1" }`
Then `decisions.dec-1.linked_trade_id` SHALL be updated to "trade-1"
And the response HTTP status SHALL be 200

#### Scenario: Link a trade from another account is rejected
Given account "acc-1" owns decision "dec-1"
And account "acc-2" owns trade "trade-99"
When account "acc-1" calls `PATCH /api/decisions/dec-1/link-trade` with `{ trade_id: "trade-99" }`
Then the response HTTP status SHALL be 404 (trade not found within caller's account)
And `linked_trade_id` SHALL remain unchanged

#### Scenario: Link a non-existent trade is rejected
Given account "acc-1" owns decision "dec-1"
When `PATCH /api/decisions/dec-1/link-trade` is called with `{ trade_id: "00000000-0000-0000-0000-000000000000" }`
Then the response HTTP status SHALL be 404

#### Scenario: Trade deleted — `linked_trade_id` becomes NULL, decision row preserved
Given account "acc-1" has decision "dec-1" with `linked_trade_id = "trade-1"`
When trade "trade-1" is deleted from the `trades` table
Then `decisions.dec-1.linked_trade_id` SHALL become NULL automatically (ON DELETE SET NULL)
And the decision row SHALL NOT be deleted
And `GET /api/decisions/dec-1` SHALL still return the decision with `linked_trade_id = null`

#### Scenario: link_decision_to_trade — happy path
Given account "acc-1" owns decision "dec-1" and trade "trade-42"
When `PATCH /api/decisions/dec-1/link-trade` is called with `{ trade_id: "trade-42" }`
Then `decisions.dec-1.linked_trade_id` SHALL be updated to "trade-42"
And the response HTTP status SHALL be 204

#### Scenario: link_decision_to_trade — clear link (null tradeId)
Given account "acc-1" owns decision "dec-1" with `linked_trade_id = "trade-42"`
When `PATCH /api/decisions/dec-1/link-trade` is called with `{ trade_id: null }`
Then `decisions.dec-1.linked_trade_id` SHALL be set to NULL
And the response HTTP status SHALL be 204

#### Scenario: link_decision_to_trade — cross-account rejection
Given account "acc-1" owns decision "dec-1"
And account "acc-2" owns trade "trade-99"
When account "acc-1" calls `PATCH /api/decisions/dec-1/link-trade` with `{ trade_id: "trade-99" }`
Then the response HTTP status SHALL be 404 (trade does not exist within acc-1's scope)
And `linked_trade_id` SHALL remain unchanged

---

### Requirement: List decisions

`GET /api/decisions` MUST return the authenticated account's decisions ordered most-recent first. Supported optional filters: `limit`, `since` (ISO 8601 date), `source`, `action`, `isin`.

#### Scenario: List decisions — default returns most recent first
Given account "acc-1" has decisions created on day 1, day 2, and day 3
When `GET /api/decisions` is called with no filters
Then the response SHALL return decisions ordered from most recent (day 3) to oldest (day 1)
And the response HTTP status SHALL be 200

#### Scenario: List decisions filtered by source
Given account "acc-1" has 3 AI_RECOMMENDATION and 2 USER_OWN_ANALYSIS decisions
When `GET /api/decisions?source=AI_RECOMMENDATION` is called
Then the response SHALL contain exactly 3 decisions
And all returned decisions SHALL have `source = "AI_RECOMMENDATION"`

#### Scenario: List decisions filtered by action
Given account "acc-1" has 4 BUY and 1 SELL decision
When `GET /api/decisions?action=SELL` is called
Then the response SHALL contain exactly 1 decision with `action = "SELL"`

#### Scenario: List decisions filtered by ISIN
Given account "acc-1" has 2 decisions for "IE00B4L5Y983" and 1 for "US0378331005"
When `GET /api/decisions?isin=IE00B4L5Y983` is called
Then the response SHALL contain exactly 2 decisions

---

### Requirement: Enum numeric stability

`DecisionAction` and `DecisionSource` MUST have explicit numeric values assigned at definition time. Future additions MUST append new values; existing values MUST NOT be reordered or renumbered. Both enums MUST be persisted as `short` in the EF entity and mapped with an explicit cast in the mapper.

#### Scenario: Persisted enum values survive future additions
Given `DecisionAction.BUY = 1` is stored in a row
When a new `DecisionAction.LIMIT_BUY = 5` is added in a future migration
Then reading the original row SHALL still deserialize correctly as `BUY`
And no data migration SHALL be required

---

### Requirement: Persona hook for source confirmation — defence in depth

The `record_decision` MCP tool docstring MUST instruct the AI model to confirm `source` with the user before invoking the tool. The `ServerInstructions` "Decision tracking (MCP)" subsection MUST instruct the AI model to ALWAYS confirm `source` before invoking `record_decision`. Both guardrails MUST be present simultaneously. One without the other is insufficient.

The tool-level instruction is machine-readable at invocation time. The `ServerInstructions` instruction is model-level and shapes conversation planning before tool selection.

#### Scenario: AI persona confirms source before invoking `record_decision`
Given a user is in an authenticated chat session
And the user says "voy a comprar 10 MSFT"
When the AI plans its next action
Then the AI MUST ask the user which `source` applies before invoking `record_decision`
And the AI MUST NOT infer source from context (e.g., inferring AI_RECOMMENDATION because the AI previously mentioned the stock)

Note: this scenario is not machine-testable at the unit level. It is verified by inspection of `ServerInstructions` and the tool docstring. The unit test that covers this requirement is: the backend MUST reject `record_decision` calls without `source` even if the AI skips asking (server-side invariant tested in the "source is required" requirement above).

---

### Requirement: `ai_chat_session_id` column — present but unpopulated in v1

The `investment_decisions` table MUST include an `ai_chat_session_id UUID NULL` column from the initial migration. In v1, this column SHALL always be NULL — it is not populated by `record_decision`. SDD #3 (chat-persistence-DB) will populate this column when available.

#### Scenario: Decision created via MCP in v1 — ai_chat_session_id is NULL
Given account "acc-1" creates a decision via the `record_decision` MCP tool in v1
When the decision row is retrieved from the database
Then `ai_chat_session_id` SHALL be NULL

---

### Requirement: Database index for track-record performance

The migration MUST include the composite index `IX_investment_decisions_account_source_status` on columns `(account_id, source, status)`. This index MUST be created in the INITIAL migration — not a follow-up migration.

#### Scenario: Migration contains the required index
Given the migration is applied to a fresh database
When the schema is inspected
Then the index `IX_investment_decisions_account_source_status` on `investment_decisions(account_id, source, status)` SHALL exist

---

### Requirement: Migration reversibility

The migration introducing `investment_decisions` MUST be reversible. Executing `dotnet ef database update <previous migration>` SHALL succeed and SHALL drop the `investment_decisions` table and related indexes without data loss to other tables.

---

### Requirement: `GET /api/market/price` endpoint accessibility

`GET /api/market/price?symbol={isin}` MUST require JWT authentication. It MUST be read-only. It MUST return the most recent price quote for the given ISIN from the `price_quotes` table.

#### Scenario: Unauthenticated request is rejected
When `GET /api/market/price?symbol=IE00B4L5Y983` is called without a JWT
Then the response HTTP status SHALL be 401

---

### Requirement: Tauri MCP allowlist

The Tauri shell `VALYZE_MCP_TOOLS` allowlist MUST include all five new MCP tools so that the Claude CLI may invoke them within a Valyze chat session.

Required additions:
- `mcp__valyze__record_decision`
- `mcp__valyze__list_decisions`
- `mcp__valyze__evaluate_decision`
- `mcp__valyze__get_decision_track_record`
- `mcp__valyze__link_decision_to_trade`

---

## Out of scope (explicitly excluded from this spec)

- Benchmark-relative evaluation (vs SPX / sector ETF) — future.
- `decision_legs` table for multi-leg REBALANCE — future. MIXED status is present in the enum but only applied to no-instrument REBALANCE in v1.
- Periodic snapshot history table (`decision_evaluations`) for track-record charts — future.
- Heuristic auto-match of trades to decisions — future background job.
- Population of `ai_chat_session_id` — deferred to SDD #3.
- UI screen for decisions in Tauri — v1 is chat/MCP only.
- Server-side suggestion worker calling `record_decision` — future Flavor 2 AI pipeline.
- `NOT_APPLICABLE` status for HOLD without instrument — resolved in design (AD-3): HOLD without instrument returns `NOT_APPLICABLE`, not `MIXED`. `MIXED` is reserved exclusively for multi-leg REBALANCE outcomes.

---

## Acceptance criteria (machine-verifiable)

- `dotnet test` MUST be green.
- New tests in `Valyze.Domain.Tests/Decisions/` MUST cover:
  - Money VO invariants on `InvestmentDecisionEntity` (currency mismatch throws).
  - `IEvaluateDecisionUseCase`: return percent calculation, ACHIEVED vs UNDERPERFORMING threshold, PENDING_HORIZON gate, NULL snapshot handling.
  - Status transitions: PENDING_HORIZON → ACHIEVED, PENDING_HORIZON → UNDERPERFORMING.
  - Default horizon resolution: BUY=180, SELL=30, HOLD=90, REBALANCE=90.
  - `DecisionSource` and `DecisionAction` enum numeric values match the table in this spec.
- Migration MUST include `IX_investment_decisions_account_source_status`.
- Migration MUST be reversible.
- `GET /api/market/price` requires JWT; returns 404 for unknown ISIN.
- `POST /api/decisions` without `source` returns HTTP 422.
- `PATCH /api/decisions/{id}/link-trade` with a cross-account trade returns HTTP 404.
- `GET /api/decisions/{id}` for a decision belonging to another account returns HTTP 404.
