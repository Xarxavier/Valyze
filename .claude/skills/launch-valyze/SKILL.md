---
name: launch-valyze
description: Local launch procedure for Valyze (backend + Tauri shell). Read this BEFORE attempting to start the stack — it skips the discovery work and goes straight to the working sequence on this maintainer's machine. Triggers on any request like "lanza valyze", "arranca el proyecto", "start the dev stack", "launch valyze", or any first-time setup question for backend / Tauri / frontend wiring on this repo.
---

# Launch Valyze (backend + Tauri)

Happy-path takes ~30 seconds: one Postgres ping + two background launches. Do **NOT** re-discover the stack — the prerequisites below have already been validated on this machine and are persisted in engram (`workflow/launch-valyze`).

## Override of preference #58

The maintainer's standing preference (engram #58) says **Claude does not start dev servers**. That preference is **suspended** for this skill — it loads only when the user explicitly says "lanza valyze" / "launch valyze" / equivalent. Do not invoke this skill on your own initiative; wait for the explicit ask.

## Assume true (do NOT re-verify)

- Postgres runs in container `eurostyle-postgres` (postgres:17, port 5432). Shared with another project. Do NOT use `backend/docker-compose.yml` — its `valyze-postgres` service collides on 5432.
- Role `valyze` and database `valyze` already exist inside that cluster (logical isolation). Connection string in `appsettings.json` is correct as-is (`Host=localhost;Port=5432;Database=valyze;Username=valyze;Password=valyze`).
- `Jwt:SigningKey` is in `dotnet user-secrets` for `Valyze.Host`.
- `frontend/node_modules` and `tauri/node_modules` exist after the first run.
- Backend listens on `http://localhost:5080`. Vite dev server on `http://localhost:1420`. Tauri shell opens its own window.

If any of the above turns out to be FALSE on this run, fall through to **Recovery** below — do not rebuild the whole investigation.

## Happy path (3 steps)

Use absolute paths. Run backend and Tauri in background.

```bash
# 1) Confirm Postgres is up (one ping, fail-fast)
docker exec -e PGPASSWORD=valyze eurostyle-postgres psql -U valyze -d valyze -tAc "SELECT 1"
# Expect: 1
```

```bash
# 2) Backend — background, wait for "Now listening on: http://localhost:5080" before step 3
cd C:/dev/github/Xarxavier/Valyze/backend && dotnet run --project src/Valyze.Host
```

```bash
# 3) Tauri shell — background; beforeDevCommand auto-starts vite on :1420
cd C:/dev/github/Xarxavier/Valyze/tauri && pnpm dev
```

Report back to the user:
- Backend task ID + `http://localhost:5080`
- Tauri task ID
- Stop instructions (`TaskStop <id>` for each; do NOT stop the Postgres container — it's shared).

## Wait pattern (between step 2 and step 3)

Watch the backend log for the readiness line; do NOT launch Tauri before this. Use a single bounded poll:

```bash
log=<backend-task-output-file>
until grep -qE "Now listening on:|error|Error|Exception" "$log" 2>/dev/null; do sleep 1; done
```

If the grep matches an error pattern instead, abort and surface the log to the user — do not launch Tauri on a half-up backend.

## Recovery (only when an assumption fails)

| Symptom | Action |
|---|---|
| `docker exec ... eurostyle-postgres` says "No such container" or it's stopped | `docker start eurostyle-postgres`; if it doesn't exist at all, ask the maintainer — there's a port-5432 sharing arrangement with another project that has to come up first. |
| `psql` reports auth failure for `valyze` | The role/db were re-created. Recreate with superuser: `docker exec -e PGPASSWORD=aaa111!!! eurostyle-postgres psql -U postgres -c "CREATE ROLE valyze LOGIN PASSWORD 'valyze'; CREATE DATABASE valyze OWNER valyze"`. |
| Port 5080 already bound | Another Valyze backend is already running. Stop it before relaunching — do NOT spin a second one. |
| Port 1420 already bound | Another Vite dev server is up. Stop it before relaunching Tauri. |
| `dotnet user-secrets list` shows no `Jwt:SigningKey` | `cd backend/src/Valyze.Host && dotnet user-secrets set Jwt:SigningKey <base64-256bit>`. Generate with `openssl rand -base64 32` or PowerShell `[Convert]::ToBase64String([byte[]](1..32 \| %{ Get-Random -Max 256 }))`. |
| `frontend/node_modules` or `tauri/node_modules` missing | `pnpm -C C:/dev/github/Xarxavier/Valyze/frontend install && pnpm -C C:/dev/github/Xarxavier/Valyze/tauri install`. Only happens on first run / after a clean. |
| Backend log shows `No migrations were applied` plus seeder skips | Normal — DB is at head. Nothing to do. |
| First-ever Tauri launch is taking minutes | Normal cold Rust build. Subsequent launches are ~15 s. |

## Stop / restart

- Backend: `TaskStop <backend-task-id>`. The .NET host releases :5080 cleanly.
- Tauri: `TaskStop <tauri-task-id>`. Kills cargo + the desktop window — but
  the **vite child process holding :1420 stays orphaned** because pnpm
  spawned it as a sibling, not a child of the supervised task. After
  TaskStop, before relaunching Tauri, free :1420:
  ```bash
  pid=$(netstat -ano | grep ":1420" | grep LISTENING | awk '{print $5}' | head -1)
  if [ -n "$pid" ]; then powershell.exe -NoProfile -Command "Stop-Process -Id $pid -Force"; fi
  ```
  Also sweep any leftover `valyze.exe` / `cargo` / msedgewebview2 children:
  ```bash
  powershell.exe -NoProfile -Command "Get-Process -Name valyze, cargo, msedgewebview2 -ErrorAction SilentlyContinue | Stop-Process -Force"
  ```
- Postgres: leave it alone. It belongs to another project.

## What NOT to do

- Do NOT run `docker compose up postgres` from `backend/` — port collision, guaranteed failure.
- Do NOT stop or modify `eurostyle-postgres` — it's shared infrastructure.
- Do NOT commit secrets discovered during recovery (passwords, JWT keys). They live in engram (`workflow/launch-valyze`) and `dotnet user-secrets`, never in the repo.
- Do NOT auto-launch this stack outside an explicit "lanza valyze"-style request. Standing preference #58 holds for everything else.
