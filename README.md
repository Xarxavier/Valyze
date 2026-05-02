# Valyze

Self-hosted, open-source investment-position analyzer. Reconstructs your portfolio from broker exports, computes the global P&L numbers your broker app probably does not show you, surfaces news that may move your holdings, and gives you a local AI mentor — backed by your own Claude subscription — to help you think about decisions.

> **Status:** early. The end-to-end loop is alive: PDF import → portfolio computation → live prices via Yahoo / FX via ECB / news via RSS → desktop chat with an AI assistant (Claude Code) that has read access to all of it. UI polish, broker coverage, and the scheduled background-suggestion pipeline are next.

## Why

Most retail brokers — Trade Republic in particular — show per-position P&L well but fall apart on the global view. You can see how Apple is doing, but the answer to *"how much have I made overall, in my account currency, with FX correctly attributed"* is buried or simply missing.

Valyze fixes that. It also adds a local AI layer (Claude Code via MCP, running on your own subscription with **zero per-query backend cost**) that pulls news and portfolio context to produce **informational analysis** about what you hold.

This is **not** a regulated investment advice service. It does not place trades. It does not move money. It analyzes data that is already yours.

## What it does

- **Ingest** trades from broker exports. Trade Republic PDF import is the first source (settlements + EX-ANTE pre-trade disclosures, German + Spanish layouts); the architecture is multi-broker by design.
- **Compute** portfolio metrics — invested, current value, P&L gross + net of sell commission, native and account-currency exposure, valuation coverage. ETF / ADR / crypto all in one view.
- **Quote** prices from free public feeds: CoinGecko (crypto), Yahoo Finance (equities/ETFs incl. UCITS mutual funds via Morningstar fallback), ECB (FX). No API keys, aggressively cached.
- **Track news** affecting your holdings via RSS (Google News + Yahoo Finance per-symbol seeded by default, more via the AI). Background poller, dedup, instrument tagging.
- **Reason** about your portfolio in a local desktop chat. Claude Code spawns a Valyze MCP server that exposes positions, trades, and news as tools, plus user-invokable skills (`/valyze:portfolio-checkup`, `/valyze:explain-position`, …). WebSearch is enabled for live research; conversations and user facts persist across sessions.
- **Suggest** *(future)* — a Hangfire-driven pipeline that calls the Anthropic API on a schedule and writes structured commentary with full prompt + tool trace for auditability. Distinct from the local chat: this one is server-side, billed per inference, and intended for the hosted SaaS mode down the line.

## Architecture

```
Tauri desktop ──HTTPS──► .NET 10 backend ──SQL──► Postgres
     │                          │
     │                          ├──► Yahoo Finance / CoinGecko (prices)
     │                          ├──► ECB                       (FX rates)
     │                          ├──► OpenFIGI                  (ISIN ↔ ticker)
     │                          └──► RSS publishers            (news)
     │
     └──► claude.exe ──stdio──► valyze-mcp ──HTTP──► same backend
              │                  (Valyze.Mcp project)
              ├──► WebSearch / WebFetch    (live research)
              └──► engram                  (cross-session user-fact memory)
```

Three top-level folders:

- `backend/` — .NET 10 solution (API, MCP server, workers, domain).
- `frontend/` — React UI, served by the Tauri shell and (later) standalone for a hosted SaaS.
- `tauri/` — Rust shell that wraps `frontend/` and bridges the desktop chat to the local Claude Code CLI.

Detailed architecture lives in [`CLAUDE.md`](./CLAUDE.md). Project-specific code rules are in [`.claude/skills/valyze-architecture/SKILL.md`](./.claude/skills/valyze-architecture/SKILL.md) — read that before writing code.

## Running it

You need:

- **.NET 10 SDK** for the backend.
- **Node.js + pnpm** for the frontend.
- **Rust toolchain** for the Tauri shell.
- **Docker** for Postgres.
- **Claude Code CLI** (`claude` on your PATH) — the desktop chat spawns it locally and uses your Claude subscription. Optional, but the chat won't work without it.

First-time setup:

```bash
# 1. Database
cd backend
docker compose up -d postgres

# 2. JWT signing key (development) — store in user-secrets, NOT in any committed file
cd src/Valyze.Host
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 32)"

# 3. Migrations + seed
cd ../..
dotnet ef database update --project src/Valyze.Infraestructure.EntityFramework --startup-project src/Valyze.Host

# 4. Backend
dotnet run --project src/Valyze.Host

# 5. Desktop app (separate terminal, from repo root)
pnpm --dir frontend install
cd tauri
cargo tauri dev
```

The personal install runs single-user with no signup flow. Bring your Trade Republic PDFs (already downloaded from the app — Valyze does not scrape Trade Republic) and drop them into the Import page.

For non-dev environments: set `Jwt:SigningKey` via environment variable (`Jwt__SigningKey` on Linux/macOS) or your secrets manager of choice. Never commit a real key.

## Distribution model

Valyze is **self-hosted open source first**. Same codebase, three modes:

1. **Personal** — one operator, one seeded account, no signup. The current target.
2. **Closed beta** — same install, signup endpoint enabled, real multi-tenant.
3. **Hosted SaaS (future)** — same backend plus a billing module.

Multi-tenancy is enforced at the data layer from the first migration; switching modes does not require a rewrite.

## Cost model

The personal install runs at **zero recurring cost**:

- Prices: free public feeds (CoinGecko, Yahoo, ECB).
- News: free RSS (Google News, Yahoo), no API keys.
- AI chat: your local Claude Code subscription (already paid). No per-query backend inference cost.
- Storage: local Postgres.

Valyze never asks for an Anthropic API key for the chat experience. The future scheduled-suggestion pipeline (Flavor 2 of the AI layer) will use a server-side API key, billed per user, and is gated behind explicit configuration.

## Contributing

Contributions are welcome. Until the project hits a tagged release, the most useful contribution is feedback on the architecture in [`CLAUDE.md`](./CLAUDE.md) — open an issue.

When opening a pull request:

1. **Open an issue first** for substantial changes. Avoid surprise PRs that touch the domain layer or the AI pipeline.
2. **Read [`.claude/skills/valyze-architecture/SKILL.md`](./.claude/skills/valyze-architecture/SKILL.md).** The architectural rules there are not stylistic preferences — they are load-bearing for correctness, multi-tenancy safety, and EU regulatory positioning.
3. **Use Conventional Commits** as documented in [`.claude/skills/valyze-commits/SKILL.md`](./.claude/skills/valyze-commits/SKILL.md). No `Co-Authored-By` or AI-attribution trailers in commits.
4. **Run the test suites** in any layer your change touches. Domain changes require unit-test coverage before merge.
5. **Respect the invariants:** money columns are `numeric`, ISIN is the instrument key, multi-tenancy is enforced at the data layer, positions are derived from trades, and P&L is reported as `(Native, Account, FxAttribution)` — always all three. PRs that soften any of these will be sent back.

## Built with

Valyze is hand-designed and hand-reviewed by its maintainer, with significant implementation help from Claude Code:

- **[Claude Code](https://claude.com/claude-code)** — the AI pair-programmer running day-to-day work on the codebase. Every architectural decision was the maintainer's; the implementation was authored collaboratively.
- **Engram** — persistent memory plugin for Claude Code that lets the assistant carry architectural decisions, conventions, and bug-fix lessons across sessions. Combined with the project's [`CLAUDE.md`](./CLAUDE.md), it's what makes a multi-week collaboration productive instead of starting cold every morning.
- **"Gentleman" output style** — the maintainer's custom Claude Code output style used during development. Defines the senior-architect tone the assistant uses while reviewing and writing code: direct, opinionated, refuses to agree without verification, prefers concept-before-code.

These tools don't appear at runtime — Valyze itself doesn't depend on Engram or any specific Claude Code style. They're called out here for transparency about how the project is built.

## License

See [`LICENCE`](./LICENCE).
