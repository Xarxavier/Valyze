use std::path::PathBuf;

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Manager};

/// One chat message round-tripped to disk. `created_at` is a JS-style
/// epoch-ms so the frontend can use it directly without conversion.
#[derive(Debug, Serialize, Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct ChatMessage {
    pub id: String,
    pub role: String, // "user" | "assistant"
    pub content: String,
    #[serde(default)]
    pub vendor: Option<String>,
    pub created_at: i64,
}

/// Full conversation as stored on disk. The `id` doubles as the
/// claude `--session-id` so loading a session and using `--resume`
/// against the same id pulls claude's own internal state too.
#[derive(Debug, Serialize, Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct ChatSession {
    pub id: String,
    pub title: Option<String>,
    pub created_at: i64,
    pub updated_at: i64,
    pub messages: Vec<ChatMessage>,
    /// Vendor used for this conversation (claude-code, future others).
    /// Stored so the picker can show a hint and so we can refuse to resume
    /// against a different vendor.
    #[serde(default)]
    pub vendor: Option<String>,
}

/// Lightweight summary for the recent-chats picker — avoids loading every
/// message into memory just to render a dropdown.
#[derive(Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ChatSessionMeta {
    pub id: String,
    pub title: Option<String>,
    pub created_at: i64,
    pub updated_at: i64,
    pub message_count: usize,
    pub vendor: Option<String>,
}

/// `<app-data>/valyze/chats/`. Created on first use. Per OS:
///   - Windows: %APPDATA%\io.valyze.desktop\chats\
///   - macOS:   ~/Library/Application Support/io.valyze.desktop/chats/
///   - Linux:   ~/.local/share/io.valyze.desktop/chats/
fn chat_dir(app: &AppHandle) -> Result<PathBuf, String> {
    let base = app.path().app_data_dir().map_err(|e| e.to_string())?;
    let dir = base.join("chats");
    std::fs::create_dir_all(&dir).map_err(|e| format!("Could not create chat dir: {e}"))?;
    Ok(dir)
}

fn session_path(dir: &std::path::Path, id: &str) -> Result<PathBuf, String> {
    // Defensive: chat ids come from the renderer; reject anything that could
    // escape the directory. Real ids are UUIDs (hex + dashes).
    if id.is_empty()
        || id.chars().any(|c| !(c.is_ascii_alphanumeric() || c == '-' || c == '_'))
    {
        return Err(format!("Invalid chat id: {id:?}"));
    }
    Ok(dir.join(format!("{id}.json")))
}

#[tauri::command]
pub async fn chat_save_session(app: AppHandle, session: ChatSession) -> Result<(), String> {
    let dir = chat_dir(&app)?;
    let path = session_path(&dir, &session.id)?;
    let body = serde_json::to_string_pretty(&session)
        .map_err(|e| format!("Could not serialize chat session: {e}"))?;
    // Atomic-ish write: write to temp then rename so a crash mid-save can't
    // leave a half-written file the next load would parse-fail on.
    let tmp = path.with_extension("json.tmp");
    tokio::fs::write(&tmp, body)
        .await
        .map_err(|e| format!("Could not write {}: {e}", tmp.display()))?;
    tokio::fs::rename(&tmp, &path)
        .await
        .map_err(|e| format!("Could not commit {}: {e}", path.display()))?;
    Ok(())
}

#[tauri::command]
pub async fn chat_load_session(
    app: AppHandle,
    id: String,
) -> Result<Option<ChatSession>, String> {
    let dir = chat_dir(&app)?;
    let path = session_path(&dir, &id)?;
    if !path.exists() {
        return Ok(None);
    }
    let body = tokio::fs::read_to_string(&path)
        .await
        .map_err(|e| format!("Could not read {}: {e}", path.display()))?;
    let session: ChatSession = serde_json::from_str(&body)
        .map_err(|e| format!("Could not parse {}: {e}", path.display()))?;
    Ok(Some(session))
}

#[tauri::command]
pub async fn chat_list_sessions(app: AppHandle) -> Result<Vec<ChatSessionMeta>, String> {
    let dir = chat_dir(&app)?;
    let mut entries = match tokio::fs::read_dir(&dir).await {
        Ok(e) => e,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(Vec::new()),
        Err(e) => return Err(format!("Could not list {}: {e}", dir.display())),
    };

    let mut metas = Vec::new();
    while let Ok(Some(entry)) = entries.next_entry().await {
        let path = entry.path();
        if path.extension().and_then(|s| s.to_str()) != Some("json") {
            continue;
        }
        match tokio::fs::read_to_string(&path).await {
            Ok(body) => match serde_json::from_str::<ChatSession>(&body) {
                Ok(session) => metas.push(ChatSessionMeta {
                    id: session.id,
                    title: session.title,
                    created_at: session.created_at,
                    updated_at: session.updated_at,
                    message_count: session.messages.len(),
                    vendor: session.vendor,
                }),
                Err(_) => {
                    // Skip corrupt files rather than aborting the whole list.
                    eprintln!("[chat_storage] skipping unreadable session: {}", path.display());
                }
            },
            Err(_) => continue,
        }
    }

    // Newest first — the picker shows the most recent at the top.
    metas.sort_by(|a, b| b.updated_at.cmp(&a.updated_at));
    Ok(metas)
}

#[tauri::command]
pub async fn chat_delete_session(app: AppHandle, id: String) -> Result<(), String> {
    let dir = chat_dir(&app)?;
    let path = session_path(&dir, &id)?;
    if !path.exists() {
        return Ok(());
    }
    tokio::fs::remove_file(&path)
        .await
        .map_err(|e| format!("Could not delete {}: {e}", path.display()))
}
