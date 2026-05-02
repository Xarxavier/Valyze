# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Product

**Valyze** is an investment-position analyzer. It ingests trade data from brokers (Trade Republic first, multi-broker by design), reconstructs the user's portfolio, and computes the analytics broker apps under-deliver — global P&L across the whole portfolio with three-way currency attribution. It also surfaces AI-generated suggestions about held positions using current news and macro context.

It produces **informational analysis**. It is **not** a regulated investment advice service (MiFID II), and the AI system prompt, UI disclaimers, and ToS must reflect that explicitly.

## Distribution Model

Self-hosted open source first. The same codebase serves three modes without rewrites:

1. **Personal** — owner runs `docker compose up`, single seeded `Account`, no signup. **Current target.**
2. **Closed beta** — same install, signup endpoint enabled, multi-user. Same schema, no migration needed.
3. **Hosted SaaS (future)** — same backend + a billing module bolted on.

Architectural consequence: **multi-tenancy is in the schema and code from day 1**, even when only one tenant exists. Every domain entity owned by a user carries `AccountId`. Repositories scope every query by `AccountId`. Query services post-validate tenant isolation via `AccountGuard.EnforceSingle/EnforceMany`. The `AccessorClassEntity` injected per request supplies the current `AccountId`.

## Architecture

```
┌──────────────────────┐    HTTPS/JWT    ┌───────────────────────────┐    SQL    ┌──────────┐
│  Tauri Desktop App   │ ──────────────► │  .NET 10 Backend          │ ────────► │ Postgres │
│  Rust + webview UI   │                 │  (Host + Workers)         │           │          │
└──────────────────────┘                 └───────────┬───────────────┘           └──────────┘
                                                     │
                                       ┌─────────────┼──────────────┐
                                       │             │              │
                                  Yahoo Finance   ECB rates    Anthropic API
                                  (prices)        (FX)         (Claude + Skills + tools)
```

The Tauri client is presentation-only: position views, P&L, suggestion cards, drag-drop PDF import. **Tauri never talks to Postgres directly** — every read/write goes through the .NET API. The auth token lives in the OS keychain (`tauri-plugin-stronghold` or `keyring`), never in the webview's localStorage.

### Repository layout

Three top-level folders:

```
backend/   .NET 10 solution
frontend/  web UI — served by Tauri AND, eventually, standalone for the hosted SaaS
tauri/     Rust shell that wraps frontend/ for desktop distribution
```

The frontend is intentionally NOT nested under `tauri/`. Same bundle must work both in the Tauri shell and in a plain browser — gate Tauri-only APIs behind feature detection.

### .NET solution layout (`backend/`)

Clean Architecture with **CQRS-light**: writes go through Repositories (EF Core), reads go through Query Services (Dapper). Domain owns ALL contracts (use case interfaces, repo interfaces, query interfaces, entities, exceptions).

```
backend/
  src/
    Valyze.Domain/                          → Entities, Enum, Exceptions, Money & Instruments VOs
                                              Application/{Feature}/I{Action}UseCase.cs   (use case interfaces)
                                              Repository/I{Aggregate}Repository.cs        (write-side ports)
                                              QueryService/I{Domain}QueryService.cs       (read-side ports)
    Valyze.Application/                     → use case implementations + ServiceExtensions
    Valyze.Infraestructure.EntityFramework/ → DbContext, EF entities, IEntityTypeConfiguration mappers,
                                              ToEf()/ToDomain() Mapper static classes
    Valyze.Infraestructure.Repository/      → EF Core repository implementations
    Valyze.Infraestructure.QueryService/    → Dapper-based query service implementations
                                              (BaseQueryService provides NpgsqlConnection)
    Valyze.Host/                            → Minimal API endpoints, JWT auth, AccessorClassEntity
                                              middleware, BusinessException handler, CORS, OpenAPI
    Valyze.Workers/                         → Hangfire host (placeholder until first scheduled job)
  tests/
    Valyze.Domain.Tests/                    → unit tests (Money invariants, etc.)
```

**Dependency direction (strict):** `Host` → `Application` → `Domain`. Each `Infraestructure.*` implements ports declared in `Domain`. `Domain` depends on nothing.

Note the Spanish spelling: `Infraestructure` (not `Infrastructure`). Mirrors the maintainer's Oregon.ControlLaboral.AI codebase — keep it consistent.

### Naming Conventions (non-negotiable)

| Concept | Pattern | Example |
|---|---|---|
| Entity | `{Name}Entity` | `AccountEntity`, `TradeEntity`, `PortfolioViewEntity` |
| Use case interface | `I{Action}UseCase` (lives in `Domain/Application/{Feature}/`) | `IGetPortfolioUseCase` |
| Use case implementation | `{Action}UseCase` (lives in `Application/{Feature}/`) | `GetPortfolioUseCase` |
| Repository interface | `I{Aggregate}Repository` (in `Domain/Repository/`) | `IAccountRepository` |
| Repository implementation | `{Aggregate}Repository` (in `Infraestructure.Repository/{Feature}/`) | `AccountRepository` |
| Query service interface | `I{Domain}QueryService` (in `Domain/QueryService/`) | `IPortfolioQueryService` |
| Query service implementation | `{Domain}QueryService` (in `Infraestructure.QueryService/{Feature}/`) | `PortfolioQueryService` |
| Endpoint group | `{Feature}Endpoints` static class with `Map{Feature}Endpoints` extension | `PortfolioEndpoints.MapPortfolioEndpoints` |
| EF entity (internal) | `{Name}` (no suffix, internal class in EF project) | `Account`, `Trade` |
| EF mapper | `{Entity}Mapper` static class with `ToEf()`/`ToDomain()` | `AccountMapper.ToEf()` |
| EF configuration | `{Entity}Configuration : IEntityTypeConfiguration<Entity>` | `AccountConfiguration` |

**DI lifetime is Scoped throughout.** No singletons except framework things (`IOptions`, etc.).

### Tauri shell (`tauri/`)

```
tauri/
  src-tauri/              → Rust core, IPC commands, OS integration (drag-drop, secure storage)
  tauri.conf.json         → points devUrl/frontendDist at ../frontend
```

### Web UI (`frontend/`)

Framework choice is low-stakes; pick one (React / Svelte / Solid) and commit. Builds to `frontend/dist/` which Tauri loads in production and a future SaaS can serve directly.

## Domain Rules (non-negotiable)

**1. `Money` is a value object with explicit currency.** Never a bare `decimal`. Adding EUR + USD throws.

```csharp
public readonly record struct Money(decimal Amount, Currency Currency);
```

**2. ISIN is the primary instrument identity.** TR uses ISIN, MiFID II requires it, and it is the only stable cross-broker key. Tickers are a secondary lookup keyed by `(isin, exchange)` — same instrument, different exchanges, different prices.

**3. Money columns are Postgres `numeric`. Never `float` / `double` / `real`.** Floating-point error compounds across thousands of trades.

**4. P&L has three faces, always reported together:**
- **Native** — in the instrument's quote currency (USD for AAPL).
- **Account** — in the account's currency (EUR for a Spanish user).
- **FX attribution** — how much of the account-currency P&L came from the instrument moving vs the FX moving.

This is the analytic Trade Republic fails at and is Valyze's core differentiator. It does not get dropped "for simplicity".

**5. Positions are derived, not stored as truth.** Trades are the source of truth. Positions are a projection (cacheable for performance). If position data and trade data ever disagree, trades win.

**6. Multi-tenancy is enforced at the data layer.** Every user-owned entity carries `AccountId`. Repositories filter by `AccountId` on every query. Query services use raw SQL with parameterized `@AccountId` and POST-VALIDATE results via `AccountGuard.EnforceSingle/EnforceMany`. API-level filtering is **not** the only line of defense.

**7. Domain anemic, behavior in use cases.** Following Oregon: domain entities are POCOs (records-like classes with public getters/setters). Validation, invariants, and orchestration live in use cases. Money / Currency / Isin remain rich VOs because their invariants are about the value itself, not about workflow.

## Data Ingestion

V1 ingestion is **PDF import from Trade Republic**:

1. User drags TR PDFs (`Wertpapierabrechnung`, `Kontoauszug`) into the Tauri app.
2. Tauri uploads the file to the backend.
3. A `Brokers.TradeRepublic` adapter (future `Valyze.Infraestructure.Brokers` project) parses the PDF into `TradeEntity` records.
4. `ITradeRepository.CreateManyAsync` persists them; portfolio analytics recompute.

Adding a new broker = a new `IBrokerAdapter` implementation under `Infraestructure.Brokers/{Name}/`. Domain stays agnostic.

**Out of scope**: scraping Trade Republic's private WebSocket API. ToS violation, and the user's downloaded PDFs are the legally clean path.

## News Ingestion

Free, no-API-keys, no-bans by design. v1 leans entirely on **RSS/Atom**: it
covers Yahoo Finance per-ticker feeds, Google News query feeds, Reddit, SEC
EDGAR, and any publisher that exposes a feed. RSS is the only protocol
*designed* to be polled, so respecting per-source intervals keeps us in
publishers' good graces with zero cost.

```
                                                ┌─ RSS feeds (Yahoo, Google News, …)
NewsCollectionService (BackgroundService)       │
  └─ IRefreshNewsUseCase ──► RSS adapter ──HTTP─┴─► news_articles + news_article_instruments
       (per-source interval guard, dedup, tagging)
```

### Schema

- **`news_sources`** — operator-managed feed list. Columns: `kind` (only `rss`
  in v1), `url_template` (supports `{symbol}` and `{name}` placeholders),
  `scope` (`PerSymbol` expands the template per held instrument; `Global`
  is fetched verbatim), `polling_interval_minutes`, `enabled`,
  `last_polled_at`, `last_error`.
- **`news_articles`** — dedup key is `url`. Title/summary stripped of HTML.
  No full body — that keeps copyright clean and the DB small.
- **`news_article_instruments`** — M:N tagging. v1 uses case-insensitive
  word-boundary match against position names + symbols (`Confidence = 0.7`)
  plus the per-fetch hint when a `PerSymbol` feed already targeted one
  instrument (`Confidence = 1.0`).

### Default sources (seeded on first run)

- "Google News — by name" → `https://news.google.com/rss/search?q={name}` (PerSymbol, 30m)
- "Yahoo Finance — by name" → `https://feeds.finance.yahoo.com/rss/2.0/headline?s={symbol}` (PerSymbol, 30m)

Both can be disabled via the API or the AI; replacing or extending them is a
one-row insert.

### AI control surface (MCP)

The Valyze MCP server exposes the news tools so the assistant can both *read*
and *curate* the feed:

| Tool                       | Purpose                                          |
| -------------------------- | ------------------------------------------------ |
| `get_news_for_symbol`      | Articles for a specific holding (ISIN/ticker).   |
| `get_latest_news`          | Latest across all holdings in the account.       |
| `list_news_sources`        | Inspect what's configured.                       |
| `add_news_source`          | Add a new RSS feed (validates URL + interval).   |
| `disable_news_source`      | Mute a noisy/broken feed.                        |
| `refresh_news`             | Force an immediate poll of every enabled source. |

Adding a tool = a method in `Valyze.Mcp/Tools/NewsTools.cs` plus the
allowlist in `tauri/src-tauri/src/claude_chat.rs`.

### Cost model

**Zero** external cost. Free RSS + free Postgres + free polling. Tagging is a
plain regex match (no LLM call). The user's local Claude subscription is the
only paid piece, and it's already paid. Smarter LLM-driven tagging is a
future enhancement, gated behind an explicit toggle so we don't accidentally
burn tokens.

### Adding a new source kind (future)

If RSS isn't enough — e.g., a JSON Feed, a custom news API, Reddit's official
JSON — drop a new project `Valyze.Infraestructure.News.<Kind>/` implementing
`INewsAdapter` with a distinct `Kind` discriminator. Register it in `Program.cs`.
The collector picks the adapter by `NewsSourceEntity.Kind`.

## Market Data

- **Prices**: Yahoo Finance (free, no SLA) covers Western markets via exchange-suffixed tickers (`.DE`, `.L`, `.PA`, `.MI`). Cache aggressively. Plan B: EOD Historical Data (paid).
- **FX**: ECB reference rates. Free, official, daily. Trade Republic uses these for tax reporting, so figures match by construction.
- **ISIN ↔ Ticker mapping**: built incrementally from PDFs; OpenFIGI as fallback.

Both feeds will populate `price_quotes` and `fx_rates` tables. Many users holding the same instrument **share one quote** — never refetch per-user.

## AI Layer

The AI layer has **two distinct flavors** that share a regulatory framing but
differ in transport, cost model, and persistence:

### Flavor 1 — Local desktop chat (current)

On-demand chat in the Tauri client, powered by the user's locally-installed
Claude Code CLI (`claude.exe`). Zero server-side AI cost — it runs against
the user's own Claude subscription. Conversations are **never persisted to
the Valyze DB**; the CLI keeps state in `~/.claude/sessions/` per chat UUID.

#### Persona & mission

The product mission ("democratize investing so beginners can act with real
confidence, not bravado") is encoded in the MCP server's `ServerInstructions`
— see `backend/src/Valyze.Mcp/Program.cs`. The assistant takes the role of
a 15-year senior portfolio mentor whose job is to **teach the user to think
about decisions**, not to make them. Hard guard-rails: no buy/sell directives,
no price predictions, no leveraged-product recommendations to beginners,
hard refusal of tax/legal advice with redirect to a qualified professional.

This persona lives at the MCP server (not Tauri's system prompt) so that
ANY MCP-aware client — Valyze chat, terminal `claude`, Cursor, future tools —
inherits the same character without per-client sync.

#### Memory & continuity

Two layers, intentional separation of concerns:

**Layer A — Conversation files** (Tauri filesystem). Each chat is a JSON
file under `<app-data>/valyze/chats/<id>.json`. The `id` doubles as
Claude Code's `--session-id`, so loading a saved chat AND issuing
`--resume <id>` to claude pulls both the visual history (frontend) and
claude's own internal context (its `~/.claude/sessions/`). Tauri commands:
`chat_save_session`, `chat_load_session`, `chat_list_sessions`,
`chat_delete_session` (see `tauri/src-tauri/src/chat_storage.rs`).
Auto-save fires after every settled turn; the frontend restores the
most recent chat on launch and exposes a "History" dropdown picker.

**Layer B — Engram for user facts** (cross-session memory). The assistant
has access to engram's `mem_save` / `mem_search` / `mem_context` /
`mem_get_observation` / `mem_judge` (allowlisted in
`tauri/src-tauri/src/claude_chat.rs`). The persona instructs it to
save USER FACTS (goals, risk tolerance, constraints, decisions + reasoning,
knowledge level signals) — and explicitly NOT chat content, position
data, or news (those have their own homes). Always scoped
`project: "valyze"` to avoid polluting other engram projects. On a
fresh chat the assistant pulls `mem_search` so its tone and assumptions
match what it already knows about the user.

#### Skills (MCP prompts)

User-invoked workflow templates that show up as slash commands in Claude Code.
Live in `backend/src/Valyze.Mcp/Prompts/InvestorPrompts.cs`. v1 ships:

| Slash command                  | What it does                                          |
| ------------------------------ | ----------------------------------------------------- |
| `/valyze:portfolio-checkup`    | Full review: holdings, P&L, concentration, news, tradeoffs. |
| `/valyze:explain-position <s>` | Deep dive on one holding (uses MCP + WebSearch).      |
| `/valyze:risk-assessment`      | Concentration, FX, asset mix, imaginary drawdown.     |
| `/valyze:daily-briefing`       | Skimmable morning summary with calendar + news.       |
| `/valyze:explain-concept <c>`  | Teach an investing concept using user's real holdings.|

Adding a slash command = a method on `[McpServerPromptType]` with
`[McpServerPrompt]`. Reflection picks it up via `WithPromptsFromAssembly()`.

```
Tauri shell ── spawns ──► claude.exe ── stdio ──► valyze-mcp.exe ── HTTP ──► Valyze.Host
   (frontend)                (CLI)                  (Valyze.Mcp)               (.NET API, :5080)
```

The model talks to the backend through an **MCP server** (`Valyze.Mcp`) that
exposes the read-only HTTP API as tools. This is how Claude knows what the
user holds: it calls `get_positions` / `get_trades` / `get_portfolio_summary`
mid-conversation.

**Where it lives**:
- `backend/src/Valyze.Mcp/` — the stdio MCP server. Console app, .NET 10,
  uses the official `ModelContextProtocol` SDK 1.2.x. Tools are
  `[McpServerTool]`-annotated methods registered automatically via
  `WithToolsFromAssembly()`. Adding a tool = adding a method.
- `tauri/src-tauri/src/claude_chat.rs` — the bridge: builds a temp
  `mcp-config.json` per session, passes `--mcp-config <path>`,
  `--tools "WebSearch WebFetch"` (those two built-ins only — no Bash/Edit/Read),
  and `--allowedTools` whitelisting `mcp__valyze__*` plus the same web tools
  so claude can only do read-only work.
- `frontend/src/components/Chat.tsx` + `frontend/src/ai/ClaudeCodeVendor.ts`
  — UI and stream parsing.

**Auth (personal mode)**: `Valyze.Mcp` calls `/auth/dev-login` on first
tool invocation, caches the JWT in process memory. Personal mode lets
dev-login be open. SaaS mode will need a different auth strategy here.

**Vendor abstraction**: the frontend has a tiny `IAiVendor` interface
(`frontend/src/ai/types.ts`); `ClaudeCodeVendor` is the first implementation.
Future vendors (OpenAI, Gemini) add a new file and a registry entry — chat
UI is vendor-agnostic.

### Flavor 2 — Background suggestion pipeline (future)

Server-side, scheduled, billed-per-user. Writes structured suggestions to
the DB. **Distinct from** the local chat: this one runs on the operator's
Anthropic API key, not the user's subscription.

```
SuggestionWorker (Hangfire, 1× per user per day, or on trade-changed event)
  └─ Anthropic Claude API agent loop
       ├─ tools: get_portfolio, get_recent_news, get_macro_indicators, get_filings
       ├─ skills: portfolio-analysis, news-summarization, risk-assessment
       └─ structured JSON output → suggestions table
```

**Cost-control rules**:
- Refresh on a schedule. Per-user inference is the most expensive thing in the system.
- **Cache shared context across users**: news per ISIN, macro indicators per region. 100 users with AAPL = 1 news fetch, not 100.

**Auditability**: every `suggestions` row stores `prompt_text`, `prompt_version`, `tools_used`, `response_json`, `model_id`.

### Shared: regulatory framing

Both flavors instruct the model that it is producing **informational analysis,
not financial advice** (MiFID II). The output schema for suggestions includes
a fixed disclaimer field; the UI repeats it.

### How Claude gets the user's positions (the canonical example)

1. User types "what's my biggest position?" in the Valyze chat.
2. Tauri spawns `claude -p "<message>" --mcp-config <tmp> --tools "" --allowedTools mcp__valyze__get_positions,...`.
3. Claude reads the tools list from the MCP server (`tools/list` JSON-RPC) and decides to invoke `get_positions`.
4. `valyze-mcp` authenticates (dev-login → JWT), calls `GET /api/positions/`, returns the JSON to Claude.
5. Claude composes a natural-language answer using the structured data.
6. The answer streams back to the Tauri UI as `stream-json` chunks.

To add another query path (e.g. "show me trades for AAPL last month"), add
a new `[McpServerTool]` method in `Valyze.Mcp/Tools/PortfolioTools.cs` and
extend `VALYZE_MCP_TOOLS` in `tauri/src-tauri/src/claude_chat.rs`. No
backend, frontend, or prompt changes required — Claude discovers the new
tool automatically from `tools/list`.

## Persistence (Postgres)

Minimal schema spine:

```
accounts          (id, email, base_currency, created_at)        -- multi-tenancy root
instruments       (isin PK, name, asset_class, quote_ccy, primary_exchange)
instrument_tickers(isin, ticker, exchange)                       -- many per ISIN
trades            (id, account_id, isin, side, quantity, price_amount, price_currency,
                   fees_amount, fees_currency, executed_at)
price_quotes      (isin, ts, price, ccy, source)                 -- shared cache
fx_rates          (base, quote, ts, rate, source)                -- shared cache (ECB)
snapshots         (account_id, ts, totals…, account_ccy)
suggestions       (account_id, ts, prompt_text, prompt_version, tools_used, response_json, model_id)
```

All money columns: `numeric(28, 8)`. All currencies: ISO 4217 codes (3-char string). EF Core migrations live in `Valyze.Infraestructure.EntityFramework`.

## Self-Hosted Operation

Required env vars in the operator's `.env` (never bundled):

- `ANTHROPIC_API_KEY` — AI layer.
- `POSTGRES_*` — connection.
- `JWT_SIGNING_KEY` — generated locally, persisted by the operator.
- Market-data API keys when a non-Yahoo provider is wired in.

No telemetry, no phone-home, no required signup for personal use.

## Build / Run / Test

- **Backend** (`backend/`):
  - `dotnet build`
  - `dotnet run --project src/Valyze.Host`
  - `dotnet test` (single test: `dotnet test --filter FullyQualifiedName~Namespace.Class.Test`)
  - `dotnet ef migrations add <Name> --project src/Valyze.Infraestructure.EntityFramework --startup-project src/Valyze.Host --output-dir Migrations`
  - `dotnet ef database update --project src/Valyze.Infraestructure.EntityFramework --startup-project src/Valyze.Host`
- **Tauri shell** (`tauri/`):
  - `cargo tauri dev` / `cargo tauri build`
  - `cargo test` (single test: `cargo test <test_name>`)
- **Frontend** (`frontend/`):
  - Package manager and dev script chosen at scaffold time (likely `pnpm dev` / `pnpm build`).
- **Local stack**: `docker compose up -d postgres` from `backend/`. Run the Host and the Tauri app separately.

## Conventions

- Conventional Commits. No AI attribution / `Co-Authored-By` trailers.
- Code, identifiers, and comments in English. Conversation with the maintainer typically Rioplatense Spanish.
- Spanish "Infraestructure" spelling is intentional — match it.
- Never commit secrets, `.env`, `appsettings.*.Local.json`, or test fixtures with real credentials.
