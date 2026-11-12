//! User settings, persisted with tauri-plugin-store in the app's data directory.

use shared::{AppError, Settings};
use tauri::{AppHandle, Runtime};
use tauri_plugin_store::StoreExt;

const FILE: &str = "settings.json";
const KEY: &str = "settings";

pub fn load<R: Runtime>(app: &AppHandle<R>) -> Settings {
    app.store(FILE)
        .ok()
        .and_then(|store| store.get(KEY))
        .and_then(|value| serde_json::from_value(value).ok())
        .unwrap_or_default()
}

pub fn save<R: Runtime>(app: &AppHandle<R>, settings: &Settings) -> Result<(), AppError> {
    let store = app.store(FILE).map_err(|e| AppError::new("settings.unavailable", e.to_string()))?;
    let value = serde_json::to_value(settings).map_err(|e| AppError::new("settings.invalid", e.to_string()))?;
    store.set(KEY, value);
    store.save().map_err(|e| AppError::new("settings.unwritable", e.to_string()))
}

/// Trims, drops a trailing slash, and adds `http://` when no scheme is given, so
/// "192.168.1.10:5000" and "http://192.168.1.10:5000/" both work.
pub fn normalize_server_url(input: &str) -> String {
    let trimmed = input.trim().trim_end_matches('/');
    if trimmed.is_empty() || trimmed.contains("://") { trimmed.to_owned() } else { format!("http://{trimmed}") }
}

#[cfg(test)]
mod tests {
    use super::normalize_server_url;

    #[test]
    fn server_urls_are_normalised() {
        assert_eq!(normalize_server_url(" 192.168.1.10:5000/ "), "http://192.168.1.10:5000");
        assert_eq!(normalize_server_url("https://music.home"), "https://music.home");
        assert_eq!(normalize_server_url(""), "");
    }
}
