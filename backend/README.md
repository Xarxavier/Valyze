# Valyze Backend

.NET 10 backend for Valyze. Clean Architecture with CQRS-light: Domain owns interfaces and entities; Application orchestrates use cases; Infraestructure is split by concern (EntityFramework, Repository, QueryService).

## Layout

```
src/
  Valyze.Domain/                          → Application/{Feature}/I{Action}UseCase.cs (use case interfaces)
                                            Entities/, Enum/, Exceptions/
                                            Repository/I{Aggregate}Repository.cs
                                            QueryService/I{Domain}QueryService.cs
                                            Money/, Instruments/  (value objects)
  Valyze.Application/                     → use case implementations + ServiceExtensions
  Valyze.Infraestructure.EntityFramework/ → DbContext, EF entities, IEntityTypeConfiguration mappers,
                                            ToEf()/ToDomain() mappers
  Valyze.Infraestructure.Repository/      → EF Core repository implementations
  Valyze.Infraestructure.QueryService/    → Dapper-based query service implementations
  Valyze.Host/                            → Minimal API endpoints, JWT auth, AccessorClassEntity middleware,
                                            BusinessException handler, CORS, OpenAPI
  Valyze.Workers/                         → Hangfire host (placeholder until first scheduled job)
tests/
  Valyze.Domain.Tests/                    → unit tests (Money invariants, etc.)
```

Dependency direction: `Host` → `Application` → `Domain`. Each `Infraestructure.*` implements ports declared in `Domain`. `Domain` depends on nothing.

## Naming conventions

| Concept | Pattern | Example |
|---|---|---|
| Entity | `{Name}Entity` | `AccountEntity`, `TradeEntity` |
| Use case interface | `I{Action}UseCase` (in `Domain/Application/`) | `IGetPortfolioUseCase` |
| Use case implementation | `{Action}UseCase` | `GetPortfolioUseCase` |
| Repository | `I{Entity}Repository` / `{Entity}Repository` | `IAccountRepository` / `AccountRepository` |
| Query service | `I{Entity}QueryService` / `{Entity}QueryService` | `IPortfolioQueryService` / `PortfolioQueryService` |
| Endpoint group | `{Feature}Endpoints` (static class with `Map{Feature}Endpoints` extension) | `PortfolioEndpoints.MapPortfolioEndpoints` |

DI lifetime is **Scoped** throughout. No singletons (except framework things like `IOptions`).

## Prerequisites

- .NET 10 SDK (pinned in `global.json`).
- Docker (for Postgres).

## Run it locally

```bash
# 1. Start Postgres
docker compose up -d postgres

# 2. Restore packages
dotnet restore

# 3. Apply the initial migration (first time only — see "Migrations" below)
dotnet ef database update \
  --project src/Valyze.Infraestructure.EntityFramework \
  --startup-project src/Valyze.Host

# 4. Run the API (default: http://localhost:5080)
dotnet run --project src/Valyze.Host
```

The API seeds a single `Account` on first boot when `Valyze:Mode = Personal`. Hit `POST /auth/dev-login` for a JWT, then call `GET /api/portfolio/` with `Authorization: Bearer <token>`.

## Migrations

Migrations are not committed to keep the scaffold clean. Generate the initial one once:

```bash
dotnet ef migrations add Initial \
  --project src/Valyze.Infraestructure.EntityFramework \
  --startup-project src/Valyze.Host \
  --output-dir Migrations
```

Then `dotnet ef database update` (step 3 above). Subsequent migrations follow the same pattern.

If `dotnet ef` is not installed: `dotnet tool install --global dotnet-ef`.

## Configuration

`appsettings.Development.json` has dev-friendly defaults. In a real install, override via environment variables:

| Variable | Purpose |
|---|---|
| `ConnectionStrings__Postgres` | Postgres connection string |
| `Jwt__SigningKey` | JWT HMAC key (256-bit minimum) |
| `Valyze__Mode` | `Personal` (default, single seeded account) or `MultiUser` |
| `Valyze__Cors__AllowedOrigins__0` | First allowed origin (e.g. `http://localhost:1420` for Tauri dev) |
| `Anthropic__ApiKey` | Anthropic API key (when AI suggestions are wired in) |

## Tests

```bash
dotnet test                                                # all
dotnet test tests/Valyze.Domain.Tests                      # one project
dotnet test --filter "FullyQualifiedName~Money"            # by name
```

## Endpoints (current)

| Method | Path | Auth | Notes |
|---|---|---|---|
| GET | `/health/` | no | Liveness probe |
| POST | `/auth/dev-login` | no | Personal mode only — returns JWT for the seeded account |
| GET | `/api/portfolio/` | yes | Stub: `{ accountId, baseCurrency, positionCount, tradeCount }` |
