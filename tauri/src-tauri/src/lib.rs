use keyring::Entry;

mod chat_storage;
mod claude_chat;

const SERVICE: &str = "io.valyze.desktop";

#[tauri::command]
fn store_token(key: String, token: String) -> Result<(), String> {
    let entry = Entry::new(SERVICE, &key).map_err(|e| e.to_string())?;
    entry.set_password(&token).map_err(|e| e.to_string())?;
    Ok(())
}

#[tauri::command]
fn get_token(key: String) -> Result<Option<String>, String> {
    let entry = Entry::new(SERVICE, &key).map_err(|e| e.to_string())?;
    match entry.get_password() {
        Ok(token) => Ok(Some(token)),
        Err(keyring::Error::NoEntry) => Ok(None),
        Err(e) => Err(e.to_string()),
    }
}

#[tauri::command]
fn clear_token(key: String) -> Result<(), String> {
    let entry = Entry::new(SERVICE, &key).map_err(|e| e.to_string())?;
    match entry.delete_credential() {
        Ok(()) => Ok(()),
        Err(keyring::Error::NoEntry) => Ok(()),
        Err(e) => Err(e.to_string()),
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .setup(|app| {
            claude_chat::register(app);
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            store_token,
            get_token,
            clear_token,
            claude_chat::claude_chat_send,
            claude_chat::claude_chat_cancel,
            claude_chat::claude_chat_available,
            chat_storage::chat_save_session,
            chat_storage::chat_load_session,
            chat_storage::chat_list_sessions,
            chat_storage::chat_delete_session,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
