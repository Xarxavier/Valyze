---
name: valyze-commits
description: Conventional Commits convention for Valyze. Read this BEFORE running `git commit`, opening a PR, or writing release notes. Defines the exact `type(scope): subject` format, the project's scope vocabulary (domain, app, host, mcp, news, brokers, marketdata, frontend, tauri, db, docs, infra, deps, …), breaking-change notation, the body/footer rules, and the project-specific bans (no `Co-Authored-By`, no AI-attribution, English-only). Triggers on any task involving git commits, pull requests, changelog generation, or release management in this repo.
---

# Valyze Commit Convention

This repo uses **[Conventional Commits 1.0.0](https://www.conventionalcommits.org/en/v1.0.0/)** strictly. Every commit message follows the same shape; the convention is what makes automated changelog and release tooling possible later.

## Format

```
<type>(<scope>)<!>: <subject>

<body>

<footer>
```

- `<type>` and `<subject>` are **required**. Everything else is optional.
- `<scope>` is parenthesised, lower-case, single token, drawn from the project vocabulary below.
- `<!>` after type/scope marks a **breaking change** (alternative: `BREAKING CHANGE:` footer).
- `<subject>` is a one-line imperative summary, ≤ 72 chars, no trailing period, lower-case verb start.
- Blank line separates header / body / footer.
- Body and footer wrap at 72 chars.

### Examples (real shapes)

```
feat(news): add RSS adapter and Hangfire-less BackgroundService poller
fix(brokers): handle multi-line LIQUIDACIÓN PDF layout for Unity / Tesla
refactor(domain): rename PortfolioSnapshotEntity → PortfolioViewEntity
docs(claude): document MCP server instructions and skills catalog
chore(deps): bump Microsoft.Extensions.* 10.0.0 → 10.0.5
ci: add dotnet test workflow for Domain.Tests
build(tauri): pin tokio to 1.x with process + io-util features
test(domain): cover Money cross-currency add throw path
perf(mcp): cache JWT for the lifetime of the MCP server process
revert: revert "feat(news): add Reddit RSS adapter"
feat(api)!: rename /api/portfolio/ → /api/portfolio-summary/
```

## Allowed types

| Type        | When to use                                                                   |
|-------------|-------------------------------------------------------------------------------|
| `feat`      | New user-facing capability or new public API surface.                         |
| `fix`       | Bug fix (any layer). Reference the symptom, not the patch line.               |
| `docs`      | Documentation only — `README.md`, `CLAUDE.md`, `SKILL.md`, code comments.     |
| `refactor`  | Code change that neither fixes a bug nor adds a feature.                      |
| `perf`      | Code change that improves performance.                                        |
| `test`      | Adding or correcting tests only.                                              |
| `build`     | Build system, package versions, project files, NuGet/Cargo/pnpm changes.      |
| `chore`     | Routine maintenance with no code or doc impact (formatting, tooling configs). |
| `ci`        | CI/CD configuration (GitHub Actions, etc.).                                   |
| `revert`    | Reverts a previous commit (subject quotes the original).                      |
| `style`     | Formatting / whitespace only (no logic). Prefer to fold into the relevant commit. |

If you can't decide between `fix` and `refactor`: was there a user-visible defect? → `fix`. Otherwise → `refactor`.

If you can't decide between `feat` and `refactor`: did the public surface (HTTP API, MCP tool, UI feature) gain something? → `feat`. Otherwise → `refactor`.

## Project scope vocabulary

Pick **one** scope per commit. Cross-cutting commits are usually a sign the change should be split — but if it's genuinely a one-shot rename or pin, drop the scope (`chore: …`).

### Backend (.NET solution under `backend/`)

| Scope          | What it covers                                                                       |
|----------------|--------------------------------------------------------------------------------------|
| `domain`       | `Valyze.Domain/` — entities, ports, VOs, enums, exceptions.                          |
| `app`          | `Valyze.Application/` — use case implementations.                                    |
| `host`         | `Valyze.Host/` — minimal API endpoints, auth, middleware, hosted services.           |
| `ef`           | `Valyze.Infraestructure.EntityFramework/` — DbContext, configurations, migrations.   |
| `repo`         | `Valyze.Infraestructure.Repository/` — write-side adapters.                          |
| `qs`           | `Valyze.Infraestructure.QueryService/` — read-side Dapper adapters.                  |
| `mcp`          | `Valyze.Mcp/` — stdio MCP server, tools, prompts.                                    |
| `news`         | `Valyze.Infraestructure.News.*/` and any News-feature work that crosses layers.      |
| `brokers`      | `Valyze.Infraestructure.Brokers.*/` — TradeRepublic and future broker adapters.      |
| `marketdata`   | `Valyze.Infraestructure.MarketData.*/` — CoinGecko, Yahoo, ECB, OpenFIGI.            |
| `workers`      | `Valyze.Workers/` — Hangfire host (placeholder until the suggestion pipeline lands). |
| `db`           | EF migrations specifically. Use this when the diff is mostly `Migrations/*.cs`.      |

### Desktop & web

| Scope     | What it covers                                                                |
|-----------|-------------------------------------------------------------------------------|
| `frontend`| `frontend/` — React UI, hooks, styling, assets.                               |
| `tauri`   | `tauri/` — Rust shell, IPC commands, `tauri.conf.json`, capabilities.         |

### Repo-wide

| Scope    | What it covers                                                            |
|----------|---------------------------------------------------------------------------|
| `docs`   | `README.md`, `CLAUDE.md`, `.claude/skills/**/SKILL.md`, code-level docs.  |
| `infra`  | `.gitignore`, `docker-compose.yml`, `global.json`, `Directory.*.props`.    |
| `ci`     | `.github/workflows/*` once it exists.                                     |
| `deps`   | Dependency bumps (NuGet, Cargo, pnpm). Combine with type `chore` or `build`. |

### When no scope fits

Drop it. `chore: rename project folder casing` is fine. Don't invent `misc:` or `repo:`.

## Subject rules

- Imperative verb, present tense: "add", "fix", "rename", "remove" — not "added" / "adds" / "adding".
- Lower-case start (after the colon).
- No trailing period.
- ≤ 72 characters. If you can't fit the *what* in 72 chars, the commit is probably doing too much.
- Reference the *outcome*, not the patch mechanics. "fix Unity name parsing" beats "use ExtractNameAboveIsin fallback".

## Body (when to write one)

Write a body when **any** of these is true:

- The *why* is non-obvious from the subject ("we use FIFO because…").
- There's a non-obvious tradeoff ("chose RSS over NewsAPI because the latter rate-limits aggressively").
- The change has known follow-ups or known limitations.
- The fix is for a specific reproducible bug — describe the trigger and the diagnosis.

Skip the body for trivial typo fixes, dep bumps, and rename-only commits.

Keep the body in plain prose. Wrap at 72 chars. Bullet lists are fine when listing distinct items.

## Footer

Use footers for:

- **`BREAKING CHANGE: <description>`** — explains an incompatible change. Equivalent to `!` after the scope; use the footer when you need more than one line.
- **`Closes #N` / `Refs #N`** — issue references when we have an issue tracker wired up.
- **`See: <url>`** — external context (an RFC, a publisher's docs, a security advisory).

## Bans (project-specific)

These are non-negotiable for Valyze:

- **No `Co-Authored-By`** trailers. Ever — even when the implementation came from Claude Code or another assistant. The maintainer is the author of every commit.
- **No AI-attribution lines** like "Generated with Claude" / "Created by AI". Same reason.
- **No emoji prefixes** (`:sparkles:`, ✨). Conventional Commits uses text types, not gitmoji.
- **No mixed-language messages.** English only in commits, even when the discussion in the PR is in Spanish.
- **No `wip:` / `tmp:` types.** Squash before merging.
- **No `--no-verify` to bypass hooks.** If a hook fails, fix the underlying issue.

## Multi-change commits

Default: **one logical change per commit**. If you're tempted to write `feat(domain, app, host): …`, split it.

Exceptions where bundling is sensible:

- A rename that touches every layer at once (`refactor: rename Money.Amount → Money.Value`).
- A migration that has to land with its model change (`feat(news)` containing the `news_articles` table + the entities + the EF config — they're meaningless apart).

## Breaking changes

Mark with `!` after the scope **and** describe the migration in the body or footer.

```
feat(api)!: rename /api/portfolio/ to /api/portfolio-summary/

Frontend now consumes /api/portfolio-summary/. The old endpoint is gone.
Self-hosted operators must update any external scripts that hit it.

BREAKING CHANGE: GET /api/portfolio/ removed; use /api/portfolio-summary/.
```

A breaking change is anything an existing operator's setup would notice without warning:

- HTTP endpoint rename / removal / contract change.
- Configuration key rename (e.g. `Jwt:SigningKey` → `Auth:SigningKey`).
- Schema change without a backwards-compatible migration path.
- MCP tool rename / removal (since other MCP clients in the wild may call them).

A breaking change is **not** a refactor that no operator can observe (renaming an internal class, splitting a use case implementation).

## Reverts

Use type `revert` and quote the reverted subject:

```
revert: feat(news): add Reddit RSS adapter

This reverts commit a1b2c3d.

Reddit's RSS endpoint is rate-limiting our user-agent more aggressively
than expected; reverting until we have a backoff strategy.
```

## Hooks

If a pre-commit hook (formatter, linter, test runner) fails, **fix the underlying issue and re-stage**. Do **not** pass `--no-verify`. The maintainer reads every PR; bypassed hooks always come up.

If the hook itself is the problem (false positive, broken config), fix the hook in a separate `chore` or `ci` commit before the work commit.

## Quick checklist before `git commit`

1. `git status` — does the staged change match the subject I'm about to write?
2. `git diff --cached` — do I understand every line I'm committing?
3. Type: am I sure it's `feat` and not `fix`? (Or vice versa.)
4. Scope: is it in the vocabulary above? If not, am I deliberately omitting it?
5. Subject: imperative, ≤ 72, lower-case, no period.
6. Body needed? If yes, did I explain *why* and not *what*?
7. Breaking? If yes, did I mark `!` and write a migration note?
8. Free of `Co-Authored-By`, AI-attribution, emoji, wip-type? Yes.

If all eight pass, commit. If any fail, fix before committing.
