# Valyze.Mcp

Stdio Model Context Protocol (MCP) server that exposes the Valyze backend HTTP
API as a set of tools any MCP-aware client can call. Built primarily so Claude
Code (`claude.exe`) can answer questions about the user's portfolio, but it
works with any compatible client (Cursor, Continue, custom agents, …).

## What it exposes

| Tool                     | What it does                                                                    |
| ------------------------ | ------------------------------------------------------------------------------- |
| `get_positions`          | Full positions view — quantity, avg cost, current value, gross & net P&L, summary. |
| `get_portfolio_summary`  | Lightweight totals (invested, position count, trade count, base currency).      |
| `get_trades [symbol]`    | Flat trade list across the portfolio, optionally filtered by ISIN / ticker.     |

Each tool method lives in `Tools/PortfolioTools.cs`. Adding one means dropping
a new method (or a new `[McpServerToolType]` class) — `Program.cs` registers
them via `WithToolsFromAssembly()`.

## Architecture

```
Claude Code  ──spawns──►  valyze-mcp.exe  ──HTTP──►  Valyze.Host (:5080)
   stdio                  (this project)               (.NET API)
```

- **Transport:** stdio (JSON-RPC over stdin/stdout).
- **Logging:** stderr only — `Console.Out` is the protocol stream and writing
  to it from outside the SDK breaks the client.
- **Auth:** on the first tool call, calls `/auth/dev-login` (open in personal
  mode) to obtain a JWT and caches it in process memory. No keychain or env
  token reads. SaaS mode will need a different strategy.
- **Config:** `VALYZE_API_BASE_URL` env var overrides the default
  `http://localhost:5080`.

## Running it

The MCP server is meant to be spawned by the client, not run by hand. The
configuration the client reads looks like this (Claude Code accepts the same
shape via `--mcp-config <file>`):

```json
{
  "mcpServers": {
    "valyze": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "<repo>/backend/src/Valyze.Mcp/Valyze.Mcp.csproj",
        "--no-build",
        "--",
        ""
      ],
      "env": {
        "VALYZE_API_BASE_URL": "http://localhost:5080"
      }
    }
  }
}
```

Production-style: `dotnet publish -c Release` produces `valyze-mcp.exe`; point
`command` at the published binary instead of `dotnet run`.

## Inside Valyze (Tauri chat)

The Tauri shell writes a temp MCP config at chat-start time and passes
`--mcp-config <tempfile>` to the spawned `claude` process — see
`tauri/src-tauri/src/claude_chat.rs`. The user never edits anything.

## Outside Valyze (terminal)

Add the same JSON snippet to your global Claude Code MCP config (e.g.
`~/.claude/mcp.json`) and the tools become available in any `claude` session
as long as the backend is running.

## Testing the server by hand

```pwsh
$env:VALYZE_API_BASE_URL = "http://localhost:5080"
dotnet run --project backend/src/Valyze.Mcp -- 2> log.txt
```

Stdin expects JSON-RPC frames; the easier path is to let an MCP client drive
it. To spot-check that the server *can* speak the protocol, send the
`initialize` request:

```
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"manual","version":"0"}}}
```

You should see a JSON response on stdout describing the server's capabilities.

## Adding a new tool

1. Open `Tools/PortfolioTools.cs` (or add a new file under `Tools/`).
2. Add a `static async Task<string>` method decorated with
   `[McpServerTool(Name = "snake_case_name")]` and `[Description(...)]`.
3. Inject `ValyzeApiClient` (or whatever you need) as a parameter — DI is in
   effect.
4. Return the JSON the model should see. Pretty-print it; the model parses
   it just fine and humans get readable output during debugging.

## Why this lives in the .NET solution

- Same tooling as the backend — only `dotnet` needs to be installed.
- Domain types are reachable if a tool ever needs to project Money / Currency
  / Isin VOs (e.g. for stricter validation of inputs).
- Single AOT-publishable executable when we ship the desktop bundle.
