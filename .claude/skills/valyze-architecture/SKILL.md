---
name: valyze-architecture
description: Architectural rules and patterns for Valyze. Read this BEFORE writing or reviewing code under backend/, frontend/, or tauri/. Establishes Clean Architecture with CQRS-light (Domain owns interfaces / entities / repositories / query services; Application implements use cases; Infraestructure split by concern), naming conventions (Entity suffix, IXxxUseCase, IXxxRepository, IXxxQueryService), Money/ISIN/numeric invariants, three-faced P&L invariant, multi-tenancy via AccountGuard + AccessorClassEntity, AI cost and audit rules, and Tauri/frontend separation. Triggers on .cs, .fs, .rs, .ts, .tsx, .jsx, .vue, .svelte, .sql files inside this repo, or any task that touches the .NET solution, the Tauri shell, or the web frontend.
---

# Valyze Architecture Skill

Read this before writing any code in this repo. The rules are not stylistic preferences — most are load-bearing for correctness, multi-tenancy safety, or EU regulatory positioning.

## Repository Surfaces

```
backend/    .NET 10 solution. Brain of the system.
frontend/   Web UI. Shipped both via the Tauri shell and (eventually) standalone for a hosted SaaS.
tauri/      Rust shell that wraps frontend/ and adds OS integration (drag-drop PDFs, secure storage, notifications).
```

The frontend is intentionally NOT nested under `tauri/`. A future web-served deployment serves `frontend/dist/` directly. Keep that boundary — `frontend/` must never import Tauri APIs unconditionally; gate them behind feature detection.

## Backend Layout — Clean Architecture + CQRS-light

```
backend/src/
  Valyze.Domain/                          ← Pure domain. Zero I/O.
    Application/{Feature}/I{Action}UseCase.cs   ← use case interfaces (NOT in Application project)
    Entities/{Feature}/{Name}Entity.cs          ← domain entities (POCOs, public set, no behavior)
    Enum/                                       ← domain enums
    Exceptions/                                 ← BusinessException, HandledException, AccountGuard
    QueryService/I{Domain}QueryService.cs       ← read-side port interfaces
    Repository/I{Aggregate}Repository.cs        ← write-side port interfaces
    Money/                                      ← Money, Currency value objects (rich, with invariants)
    Instruments/                                ← Isin value object

  Valyze.Application/                     ← Use case implementations.
    {Feature}/{Action}UseCase.cs
    ServiceExtensions.cs (AddValyzeApplication)

  Valyze.Infraestructure.EntityFramework/ ← DbContext + EF entities + mappers.
    ValyzeDbContext.cs
    Entities/{Name}.cs                          ← EF entities (internal, no Entity suffix here)
    Mapper/{Entity}Configuration.cs             ← IEntityTypeConfiguration<EfEntity>
    Mapper/{Entity}Mapper.cs                    ← ToEf() / ToDomain() static methods
    ServiceExtensions.cs (AddValyzeEntityFramework)

  Valyze.Infraestructure.Repository/      ← EF Core write-side adapters.
    {Feature}/{Aggregate}Repository.cs
    ServiceExtensions.cs (AddValyzeRepositories)

  Valyze.Infraestructure.QueryService/    ← Dapper read-side adapters.
    BaseQueryService.cs                         ← provides NpgsqlConnection
    {Feature}/{Domain}QueryService.cs           ← inherits BaseQueryService, parameterized SQL
    ServiceExtensions.cs (AddValyzeQueryServices)

  Valyze.Host/                            ← API surface. Tauri-facing.
    Program.cs                                  ← chains Add* extension methods
    ServiceExtensions.cs (AddValyzeHost)
    MinimalApi/{Feature}/{Feature}Endpoints.cs  ← static class + Map{Feature}Endpoints extension
    MinimalApi/MapMinimalApiExtensions.cs       ← chains all endpoint groups
    Authorization/JwtTokenService.cs            ← issues JWTs (implements IJwtTokenService)
    Authorization/AccessorClassMiddleware.cs    ← populates AccessorClassEntity per request
    Authorization/BusinessExceptionHandler.cs   ← IExceptionHandler — maps BusinessException → 400
    Configuration/ValyzeOptions.cs, JwtOptions
    Setup/SeedRunner.cs

  Valyze.Workers/                         ← Hangfire host. Empty until first scheduled job.
```

**Dependency direction (strict):** `Host` → `Application` → `Domain`. Each `Infraestructure.*` project implements ports declared in `Domain`. `Workers` depends on `Application` + `Infraestructure.*`. **`Domain` depends on nothing** — not even logging frameworks.

Spanish "Infraestructure" spelling is intentional — match the maintainer's Oregon codebase.

## Naming Conventions (non-negotiable)

| Concept | Pattern | Example |
|---|---|---|
| Domain entity | `{Name}Entity` | `AccountEntity`, `TradeEntity`, `PortfolioViewEntity` |
| Use case interface | `I{Action}UseCase` in `Domain/Application/{Feature}/` | `IGetPortfolioUseCase`, `IImportTradesUseCase` |
| Use case implementation | `{Action}UseCase` in `Application/{Feature}/` | `GetPortfolioUseCase` |
| Repository interface | `I{Aggregate}Repository` in `Domain/Repository/` | `IAccountRepository`, `ITradeRepository` |
| Repository implementation | `{Aggregate}Repository` in `Infraestructure.Repository/{Feature}/` | `AccountRepository` |
| Query service interface | `I{Domain}QueryService` in `Domain/QueryService/` | `IPortfolioQueryService` |
| Query service implementation | `{Domain}QueryService` in `Infraestructure.QueryService/{Feature}/` | `PortfolioQueryService` |
| Endpoint group | `{Feature}Endpoints` static + `Map{Feature}Endpoints` extension | `PortfolioEndpoints.MapPortfolioEndpoints` |
| EF entity (internal) | `{Name}` (no suffix) | `Account`, `Trade` (internal in EF project) |
| EF mapper | `{Entity}Mapper` static with `ToEf()` / `ToDomain()` | `AccountMapper.ToEf()` |
| EF configuration | `{Entity}Configuration : IEntityTypeConfiguration<EfEntity>` | `AccountConfiguration` |
| Domain VO | no suffix, struct/record | `Money`, `Currency`, `Isin` |

**DI lifetime is Scoped everywhere.** No singletons except framework primitives (`IOptions`, etc.). Each project exposes one `Add{Project}` extension method, chained in `Program.cs`.

## Domain Invariants — Do Not Soften

### 1. `Money` is a value object with explicit currency

```csharp
public readonly record struct Money(decimal Amount, Currency Currency)
{
    public static Money operator +(Money a, Money b) =>
        a.Currency == b.Currency
            ? new Money(a.Amount + b.Amount, a.Currency)
            : throw new InvalidOperationException($"Cannot add {a.Currency} and {b.Currency}");
}
```

Never accept a bare `decimal` for monetary values in domain or application layers. Postgres columns: `numeric(28, 8)`. Currencies are ISO 4217 codes.

### 2. ISIN is the primary instrument identity

`TradeEntity` and other instrument-bearing entities key off `Isin`. Tickers are a secondary index `(isin, exchange)` because the same instrument trades on multiple exchanges with different prices and currencies. MiFID II requires ISIN.

### 3. Multi-tenancy is enforced via repos AND query services

Every user-owned entity carries `AccountId` (Guid). Two layers of defense:

- **Repositories**: every query and mutation filters by `AccountId`. Mutations also validate `entity.AccountId == accessor.AccountId` before persisting.
- **Query services**: SQL always parameterizes `@AccountId`. After fetching, post-validate using `AccountGuard.EnforceSingle(entity, accountId, e => e.AccountId)` (or `EnforceMany` for collections). This catches a missing WHERE clause that somehow got past review.

`AccessorClassEntity` is the per-request scoped service that carries `AccountId` from the JWT. `AccessorClassMiddleware` populates it. Endpoints inject `AccessorClassEntity` as a parameter.

### 4. Positions are derived, not stored as truth

`trades` is the source of truth. Positions are a projection — cacheable, always recomputable. If position data and trade data ever disagree, trades win.

### 5. P&L has three faces, always reported together

```csharp
public sealed class PnL
{
    public Money Native { get; set; }            // in instrument quote currency
    public Money Account { get; set; }           // in account currency
    public FxAttribution Split { get; set; }     // price-driven vs FX-driven
}
```

Valyze's product differentiator. API responses, UI components, and analytics MUST surface all three. Dropping FX attribution "for simplicity" is not allowed.

### 6. Domain entities are anemic. Behavior lives in use cases.

Domain entity classes are POCOs (records-like classes with public setters). Validation, invariants, and orchestration live in `{Action}UseCase` implementations. Money / Currency / Isin keep their invariants because those are about the *value*, not the workflow.

## Read-side Pattern — Query Services

```csharp
public class PortfolioQueryService : BaseQueryService, IPortfolioQueryService
{
    public PortfolioQueryService(IConfiguration config) : base(config) { }

    public async Task<PortfolioViewEntity> GetViewAsync(Guid accountId, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        var view = await connection.QueryFirstOrDefaultAsync<PortfolioViewEntity>(
            "SELECT … FROM accounts WHERE id = @AccountId",
            new { AccountId = accountId })
            ?? throw new BusinessException("msnAccountNotFound");
        return AccountGuard.EnforceSingle(view, accountId, v => v.AccountId);
    }
}
```

Rules:
- **Always** parameterize. Never string-concatenate user input.
- **Always** post-validate with `AccountGuard`.
- Connection is `using` — Dapper cleans up after the scope.
- Throw `BusinessException("msnXxx")` on missing-but-expected data; let the global handler render 400.

## Write-side Pattern — Repositories

```csharp
public class AccountRepository : IAccountRepository
{
    private readonly ValyzeDbContext _context;
    public AccountRepository(ValyzeDbContext context) => _context = context;

    public async Task<AccountEntity> CreateAsync(AccountEntity account, CancellationToken ct = default)
    {
        var ef = AccountMapper.ToEf(account);
        _context.Accounts.Add(ef);
        await _context.SaveChangesAsync(ct);
        return AccountMapper.ToDomain(ef);
    }
}
```

Rules:
- Map domain → EF on the way in, EF → domain on the way out.
- EF entities are `internal` to `Valyze.Infraestructure.EntityFramework`. Repositories in the sibling project access them via `internal` visibility within the assembly group, OR via `internal` exposed through `InternalsVisibleTo` if needed. Simpler: leave EF entities `internal` and have repositories live where they can see them, or open them up — choose at refactor time.

## AI Layer Rules

- **Anthropic SDK + agent loop + Agent Skills.** NOT a wrapper around the Claude Code CLI.
- **Hangfire-scheduled.** Never on-demand without rate limit.
- **Cache shared context** across users (news per ISIN, macro per region).
- **Auditability**: every `suggestions` row stores `prompt_text`, `prompt_version`, `tools_used`, `response_json`, `model_id`.
- **Regulatory framing in every prompt**: informational analysis, not financial advice. Output schema includes a fixed disclaimer field.

## Tauri / Frontend Rules

- **Frontend is presentation-only.** No analytics, no P&L computation, no business logic beyond display formatting.
- **All API calls hit the .NET backend over HTTPS.** The client NEVER talks to Postgres directly.
- **JWT lives in OS keychain** (`tauri-plugin-stronghold` or `keyring`).
- **PDF import flow:** drag-drop in webview → Tauri command → upload to backend → backend parses.
- **Tauri-only features behind feature detection** so `frontend/` works in both Tauri and a plain browser.

## Testing

- **Domain:** pure unit tests, no infrastructure.
- **Application:** use case tests with port fakes (NSubstitute or hand-written).
- **Infraestructure:** integration tests against a real Postgres via Testcontainers or `docker compose`.
- **Host:** thin endpoint smoke tests.
- **Frontend / Tauri:** smoke tests for golden paths.

A multi-tenancy guard test is mandatory: assert that `AccountGuard.EnforceSingle` is invoked in every query service method that returns user-owned data. Assert repositories filter by `AccountId` on every read.

## Style

- Conventional Commits. No AI-attribution / `Co-Authored-By` trailers.
- Code, identifiers, and comments in English. Conversation in PRs/issues can be Rioplatense Spanish or English.
- Spanish "Infraestructure" spelling is intentional. Match it.
- Comments only for non-obvious *why*. No commentary that restates the code.
- One `Add{Project}` extension method per project. Chain in `Program.cs`.

## When in Doubt

- Will this make multi-tenancy harder to enforce? → reject.
- Does this hide one of the three P&L numbers? → reject.
- Does this couple the domain to a framework? → reject.
- Does this turn an inferred (derived) value into stored truth? → reject unless there's a measured perf reason and a rebuild path.
- Does this leak Tauri-specific APIs into `frontend/` without a fallback? → reject.
- Is a use case interface in `Application/` instead of `Domain/Application/`? → reject.
- Is an entity missing the `Entity` suffix? → reject.
- Is a query service skipping `AccountGuard`? → reject.
