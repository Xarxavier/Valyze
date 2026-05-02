use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::sync::Arc;

use serde::Deserialize;
use tauri::{AppHandle, Emitter, Manager, State};
use tokio::io::{AsyncBufReadExt, BufReader};
use tokio::process::{Child, Command};
use tokio::sync::Mutex;

/// Windows: spawning a console binary from a GUI process pops a black
/// console window AND can confuse stdio inheritance. CREATE_NO_WINDOW
/// (0x08000000) suppresses it. No-op on other platforms.
#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

fn apply_platform_quirks(_cmd: &mut Command) {
    #[cfg(windows)]
    {
        _cmd.creation_flags(CREATE_NO_WINDOW);
    }
}

/// In-flight claude.exe processes keyed by the frontend-supplied request id.
/// Used to cancel a generation when the user clicks Stop.
#[derive(Default)]
pub struct ChatProcesses(pub Arc<Mutex<HashMap<String, Child>>>);

/// Names the Valyze MCP server exposes. Kept in sync with
/// `backend/src/Valyze.Mcp/Tools/*.cs`. Whitelisted via `--allowedTools` so
/// claude runs them without prompting the user. All current tools are
/// either read-only or affect news-source rows in the user's own DB —
/// safe to auto-allow.
const VALYZE_MCP_TOOLS: &[&str] = &[
    // Portfolio (read-only)
    "mcp__valyze__get_positions",
    "mcp__valyze__get_portfolio_summary",
    "mcp__valyze__get_trades",
    // News (read + curate)
    "mcp__valyze__get_news_for_symbol",
    "mcp__valyze__get_latest_news",
    "mcp__valyze__list_news_sources",
    "mcp__valyze__add_news_source",
    "mcp__valyze__disable_news_source",
    "mcp__valyze__refresh_news",
];

/// Built-in Claude Code tools we do allow. WebSearch + WebFetch let the
/// assistant reach beyond the local news cache for primers, regulatory
/// updates, sector trends, etc. Both are read-only against the public
/// internet — safe to auto-allow. We stay away from filesystem (Read,
/// Edit) and shell (Bash) so the chat can never touch the host machine.
const ALLOWED_BUILTIN_TOOLS: &[&str] = &["WebSearch", "WebFetch"];

/// Engram tools we allow when the user has the Engram plugin installed in
/// their global Claude Code config. Used by the assistant to remember and
/// recall USER FACTS (goals, risk tolerance, constraints) across sessions
/// — see the persona block in Valyze.Mcp/Program.cs for the policy on what
/// to save. Whitelisting these is a no-op when Engram isn't installed
/// (claude simply won't see the tools).
///
/// We deliberately exclude destructive ones (mem_delete, mem_update) and
/// session-lifecycle ones — those should be explicit user actions, not
/// silent agentic behaviour.
const ALLOWED_ENGRAM_TOOLS: &[&str] = &[
    "mcp__plugin_engram_engram__mem_save",
    "mcp__plugin_engram_engram__mem_search",
    "mcp__plugin_engram_engram__mem_context",
    "mcp__plugin_engram_engram__mem_get_observation",
    "mcp__plugin_engram_engram__mem_judge",
];

/// Walks up from the Tauri executable looking for the Valyze.Mcp project file.
/// In dev mode, current_exe is `tauri/src-tauri/target/debug/valyze.exe`, so
/// the marker `backend/src/Valyze.Mcp/Valyze.Mcp.csproj` is reachable a few
/// levels up. The override env var `VALYZE_MCP_PROJECT` short-circuits this
/// for users who run a published build from a different layout.
fn find_mcp_project() -> Option<PathBuf> {
    if let Ok(explicit) = std::env::var("VALYZE_MCP_PROJECT") {
        let p = PathBuf::from(explicit);
        if p.exists() {
            return Some(p);
        }
    }
    let exe = std::env::current_exe().ok()?;
    let mut dir: &Path = exe.parent()?;
    for _ in 0..10 {
        let candidate = dir.join("backend/src/Valyze.Mcp/Valyze.Mcp.csproj");
        if candidate.exists() {
            return Some(candidate);
        }
        dir = dir.parent()?;
    }
    None
}

/// Writes a temp `mcp-config.json` Claude Code can load with `--mcp-config`.
/// One file per chat session is fine — the JSON is tiny and overwriting it
/// each turn keeps the path deterministic.
fn write_mcp_config(session_id: &str) -> Result<PathBuf, String> {
    let project = find_mcp_project()
        .ok_or("Could not locate Valyze.Mcp project. Set VALYZE_MCP_PROJECT to its .csproj path.")?;
    let api_base = std::env::var("VALYZE_API_BASE_URL")
        .unwrap_or_else(|_| "http://localhost:5080".to_string());

    let payload = serde_json::json!({
        "mcpServers": {
            "valyze": {
                "command": "dotnet",
                "args": [
                    "run",
                    "--project",
                    project.to_string_lossy(),
                    "--no-build",
                    "--no-launch-profile",
                    "--"
                ],
                "env": {
                    "VALYZE_API_BASE_URL": api_base
                }
            }
        }
    });

    let mut path = std::env::temp_dir();
    path.push(format!("valyze-mcp-{session_id}.json"));
    let body = serde_json::to_string_pretty(&payload)
        .map_err(|e| format!("Could not serialize MCP config: {e}"))?;
    std::fs::write(&path, body).map_err(|e| format!("Could not write MCP config: {e}"))?;
    Ok(path)
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ChatSendRequest {
    /// Frontend-generated id used for cancellation and event routing.
    pub request_id: String,
    /// UUID for the conversation. The very first turn opens the session;
    /// subsequent turns resume it so claude keeps the context locally.
    pub session_id: String,
    pub is_first_turn: bool,
    /// User's message for this turn.
    pub prompt: String,
    /// Portfolio snapshot + role guidance. Only attached on the first turn.
    pub system_prompt: Option<String>,
}

/// Spawns `claude -p` with stream-json output and forwards every NDJSON line
/// to the frontend as a `chat:chunk:<request_id>` event. Emits
/// `chat:done:<request_id>` (success) or `chat:error:<request_id>` on exit.
#[tauri::command]
pub async fn claude_chat_send(
    app: AppHandle,
    state: State<'_, ChatProcesses>,
    req: ChatSendRequest,
) -> Result<(), String> {
    // Generate the MCP config file pointing at the Valyze.Mcp project. We
    // do this every turn so picking up code changes during dev is automatic;
    // the file is tiny and lives under temp_dir.
    let mcp_config_path = match write_mcp_config(&req.session_id) {
        Ok(p) => Some(p),
        Err(e) => {
            // Soft failure — chat still works, just without MCP tools. The
            // model falls back to the system-prompt portfolio snapshot.
            eprintln!("[claude_chat] MCP config not available: {e}");
            None
        }
    };

    let mut cmd = Command::new("claude");

    // Common flags for non-interactive streamed output.
    cmd.arg("-p")
        .arg(&req.prompt)
        .arg("--output-format")
        .arg("stream-json")
        .arg("--verbose");

    // Whitelist only the built-in tools we want (WebSearch, WebFetch). Bash,
    // Edit, Read — anything that touches the user's machine — stays disabled.
    // Combined with --allowedTools below, claude can ONLY call our MCP server's
    // methods plus the two web-research tools we explicitly listed.
    cmd.arg("--tools").arg(ALLOWED_BUILTIN_TOOLS.join(" "));

    if let Some(ref cfg) = mcp_config_path {
        cmd.arg("--mcp-config").arg(cfg);
        let mut allowlist: Vec<&str> = VALYZE_MCP_TOOLS.to_vec();
        allowlist.extend(ALLOWED_BUILTIN_TOOLS.iter().copied());
        allowlist.extend(ALLOWED_ENGRAM_TOOLS.iter().copied());
        cmd.arg("--allowedTools").arg(allowlist.join(","));
    } else {
        // No MCP config means the chat falls back to web + engram tools only
        // — still useful as a degraded mode.
        let mut allowlist: Vec<&str> = ALLOWED_BUILTIN_TOOLS.to_vec();
        allowlist.extend(ALLOWED_ENGRAM_TOOLS.iter().copied());
        cmd.arg("--allowedTools").arg(allowlist.join(","));
    }

    if req.is_first_turn {
        cmd.arg("--session-id").arg(&req.session_id);
        if let Some(sys) = &req.system_prompt {
            if !sys.is_empty() {
                cmd.arg("--append-system-prompt").arg(sys);
            }
        }
    } else {
        cmd.arg("--resume").arg(&req.session_id);
    }

    cmd.stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .kill_on_drop(true);
    apply_platform_quirks(&mut cmd);

    let mut child = cmd.spawn().map_err(|e| {
        eprintln!("[claude_chat] spawn failed: {e:?}");
        format!(
            "Could not start claude.exe: {e}. Is the `claude` CLI installed and on PATH for this process?"
        )
    })?;

    let stdout = child.stdout.take().ok_or("Failed to capture stdout")?;
    let stderr = child.stderr.take().ok_or("Failed to capture stderr")?;

    {
        let mut map = state.0.lock().await;
        map.insert(req.request_id.clone(), child);
    }

    let request_id = req.request_id.clone();
    let app_for_task = app.clone();
    let processes = state.0.clone();

    tokio::spawn(async move {
        let chunk_event = format!("chat:chunk:{request_id}");
        let done_event = format!("chat:done:{request_id}");
        let error_event = format!("chat:error:{request_id}");

        let stdout_app = app_for_task.clone();
        let stdout_evt = chunk_event.clone();
        let stdout_task = tokio::spawn(async move {
            let mut reader = BufReader::new(stdout).lines();
            while let Ok(Some(line)) = reader.next_line().await {
                let _ = stdout_app.emit(&stdout_evt, line);
            }
        });

        // Buffer stderr so we can surface a useful error if the process fails.
        let stderr_task = tokio::spawn(async move {
            let mut buf = String::new();
            let mut reader = BufReader::new(stderr).lines();
            while let Ok(Some(line)) = reader.next_line().await {
                if !buf.is_empty() {
                    buf.push('\n');
                }
                buf.push_str(&line);
            }
            buf
        });

        // Reclaim the child to await its exit. The map entry is removed so a
        // late `claude_chat_cancel` call becomes a no-op instead of killing
        // the next request that reuses the id.
        let mut owned_child = {
            let mut map = processes.lock().await;
            match map.remove(&request_id) {
                Some(c) => c,
                None => {
                    // Cancelled before we got here; nothing else to do.
                    let _ = app_for_task.emit(&done_event, ());
                    return;
                }
            }
        };

        let exit = owned_child.wait().await;
        let _ = stdout_task.await;
        let stderr_text = stderr_task.await.unwrap_or_default();

        match exit {
            Ok(status) if status.success() => {
                let _ = app_for_task.emit(&done_event, ());
            }
            Ok(status) => {
                let payload = if stderr_text.is_empty() {
                    format!("claude exited with status {status}")
                } else {
                    format!("claude exited with status {status}: {stderr_text}")
                };
                let _ = app_for_task.emit(&error_event, payload);
            }
            Err(e) => {
                let _ = app_for_task.emit(&error_event, format!("Failed to wait on claude: {e}"));
            }
        }
    });

    Ok(())
}

#[tauri::command]
pub async fn claude_chat_cancel(
    state: State<'_, ChatProcesses>,
    request_id: String,
) -> Result<(), String> {
    let child = {
        let mut map = state.0.lock().await;
        map.remove(&request_id)
    };
    if let Some(mut child) = child {
        let _ = child.start_kill();
        let _ = child.wait().await;
    }
    Ok(())
}

/// Cheap reachability probe — `claude --version` runs to completion fast.
/// The frontend uses this to decide whether to surface "claude not found"
/// before the user sends their first message.
#[tauri::command]
pub async fn claude_chat_available() -> Result<bool, String> {
    let mut cmd = Command::new("claude");
    cmd.arg("--version")
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null());
    apply_platform_quirks(&mut cmd);
    match cmd.spawn() {
        Ok(mut child) => Ok(child.wait().await.map(|s| s.success()).unwrap_or(false)),
        Err(e) => {
            eprintln!("[claude_chat] availability probe failed: {e:?}");
            Ok(false)
        }
    }
}

pub fn register(app: &mut tauri::App) {
    app.manage(ChatProcesses::default());
}
