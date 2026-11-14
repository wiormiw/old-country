//! Tauri commands: thin wrappers the UI invokes. Server calls go to [`ApiClient`]; playback goes
//! to the player thread, whose state comes back as events rather than return values.

use std::sync::{Arc, Mutex};

use shared::{
    AppError, DownloadStatus, Downloads, ImportReport, Paged, PlayerState, Playlist, PlaylistDownloadReport, PlaylistSummary, QueueItem, RepeatMode,
    LiveStatus, ScanStatus, Settings, Theme, Track, TrackDetail, YouTubeResult,
};
use tauri::{AppHandle, Manager, State};
use tauri_plugin_opener::OpenerExt;

use crate::api::ApiClient;
use crate::presence::Presence;
use crate::player::cache::AudioCache;
use crate::player::{Command, PlayerHandle, ServerUrl};
use crate::settings;

const MEGABYTE: u64 = 1024 * 1024;

pub struct AppState {
    pub api: ApiClient,
    pub player: PlayerHandle,
    pub server: ServerUrl,
    pub settings: Mutex<Settings>,
    pub cache: Arc<AudioCache>,
    /// The live connection's latest status (also pushed as an event when it changes).
    pub live: Arc<Mutex<LiveStatus>>,
    /// The Discord card. `None` when the Discord application id is not set.
    pub presence: Option<Presence>,
}

type Reply<T> = Result<T, AppError>;

/// Whether changes made elsewhere are arriving live right now.
#[tauri::command]
pub fn live_status(state: State<'_, AppState>) -> LiveStatus {
    *state.live.lock().expect("live status lock")
}

// --- Settings ---

#[tauri::command]
pub fn get_settings(state: State<'_, AppState>) -> Settings {
    state.settings.lock().expect("settings lock").clone()
}

/// Checks the server, then saves. A server that doesn't answer isn't saved.
#[tauri::command]
pub async fn save_settings(app: AppHandle, state: State<'_, AppState>, settings: Settings) -> Reply<Settings> {
    let mut settings = settings;
    settings.server_url = settings::normalize_server_url(&settings.server_url);
    settings.cache_max_megabytes = settings.cache_max_megabytes.clamp(128, 1024 * 1024);
    if !settings.server_url.is_empty() {
        state.api.check_server(&settings.server_url).await?;
    }
    settings::save(&app, &settings)?;
    *state.server.write().expect("server url lock") = settings.server_url.clone();
    *state.settings.lock().expect("settings lock") = settings.clone();
    state.cache.trim(settings.cache_max_megabytes * MEGABYTE);
    if let Some(presence) = &state.presence {
        presence.set_enabled(settings.discord_presence);
    }
    Ok(settings)
}

#[tauri::command]
pub async fn check_server(state: State<'_, AppState>, url: String) -> Reply<()> {
    state.api.check_server(&settings::normalize_server_url(&url)).await
}

// --- Library ---

#[tauri::command]
pub async fn list_tracks(
    state: State<'_, AppState>,
    page: u32,
    page_size: u32,
    search: Option<String>,
) -> Reply<Paged<Track>> {
    state.api.list_tracks(page, page_size, search.as_deref()).await
}

#[tauri::command]
pub async fn get_track(state: State<'_, AppState>, id: String) -> Reply<TrackDetail> {
    state.api.get_track(&id).await
}

// --- Logs ---

/// Where the desktop app writes its log file.
#[tauri::command]
pub fn logs_folder(app: AppHandle) -> Reply<String> {
    app.path()
        .app_log_dir()
        .map(|dir| dir.display().to_string())
        .map_err(|e| AppError::new("logs.unavailable", e.to_string()))
}

/// Opens the log folder in the file manager.
#[tauri::command]
pub fn open_logs_folder(app: AppHandle) -> Reply<()> {
    let dir = app.path().app_log_dir().map_err(|e| AppError::new("logs.unavailable", e.to_string()))?;
    std::fs::create_dir_all(&dir).map_err(|e| AppError::new("logs.unavailable", e.to_string()))?;
    app.opener()
        .open_path(dir.display().to_string(), None::<&str>)
        .map_err(|e| AppError::new("logs.unavailable", e.to_string()))
}

#[tauri::command]
pub async fn scan_library(state: State<'_, AppState>) -> Reply<ScanStatus> {
    state.api.scan_library().await
}

#[tauri::command]
pub async fn scan_status(state: State<'_, AppState>) -> Reply<ScanStatus> {
    state.api.scan_status().await
}

// --- Playlists ---

#[tauri::command]
pub async fn list_playlists(state: State<'_, AppState>) -> Reply<Vec<PlaylistSummary>> {
    state.api.list_playlists().await
}

#[tauri::command]
pub async fn get_playlist(state: State<'_, AppState>, id: String) -> Reply<Playlist> {
    state.api.get_playlist(&id).await
}

#[tauri::command]
pub async fn create_playlist(state: State<'_, AppState>, name: String, description: Option<String>) -> Reply<PlaylistSummary> {
    state.api.create_playlist(&name, description.as_deref()).await
}

#[tauri::command]
pub async fn update_playlist(
    state: State<'_, AppState>,
    id: String,
    name: String,
    description: Option<String>,
) -> Reply<PlaylistSummary> {
    state.api.update_playlist(&id, &name, description.as_deref()).await
}

#[tauri::command]
pub async fn delete_playlist(state: State<'_, AppState>, id: String) -> Reply<()> {
    state.api.delete_playlist(&id).await
}

#[tauri::command]
pub async fn add_playlist_tracks(state: State<'_, AppState>, id: String, track_ids: Vec<String>) -> Reply<PlaylistSummary> {
    state.api.add_playlist_tracks(&id, &track_ids).await
}

#[tauri::command]
pub async fn remove_playlist_entry(state: State<'_, AppState>, id: String, position: u32) -> Reply<()> {
    state.api.remove_playlist_entry(&id, position).await
}

#[tauri::command]
pub async fn move_playlist_entry(state: State<'_, AppState>, id: String, from: u32, to: u32) -> Reply<()> {
    state.api.move_playlist_entry(&id, from, to).await
}

// --- YouTube Music ---

#[tauri::command]
pub async fn youtube_search(state: State<'_, AppState>, query: String, limit: u32) -> Reply<Vec<YouTubeResult>> {
    state.api.youtube_search(&query, limit).await
}

#[tauri::command]
pub async fn youtube_download(state: State<'_, AppState>, video_ids: Vec<String>) -> Reply<Vec<DownloadStatus>> {
    state.api.youtube_download(&video_ids).await
}

#[tauri::command]
pub async fn youtube_downloads(state: State<'_, AppState>) -> Reply<Downloads> {
    state.api.youtube_downloads().await
}

#[tauri::command]
pub async fn download_playlist(state: State<'_, AppState>, id: String, retry_skipped: bool) -> Reply<PlaylistDownloadReport> {
    state.api.download_playlist(&id, retry_skipped).await
}

#[tauri::command]
pub async fn sync_playlist(state: State<'_, AppState>, id: String) -> Reply<ImportReport> {
    state.api.sync_playlist(&id).await
}

#[tauri::command]
pub async fn youtube_import(state: State<'_, AppState>, playlist: String) -> Reply<ImportReport> {
    state.api.youtube_import(&playlist).await
}

// --- Player (state arrives through the player://state event) ---

#[tauri::command]
pub fn player_state(state: State<'_, AppState>) -> PlayerState {
    state.player.state()
}

#[tauri::command]
pub fn player_play(state: State<'_, AppState>, items: Vec<QueueItem>, start: usize, context: Option<String>) {
    state.player.send(Command::Play { items, start, context });
}

#[tauri::command]
pub fn player_enqueue(state: State<'_, AppState>, items: Vec<QueueItem>) {
    state.player.send(Command::Enqueue(items));
}

#[tauri::command]
pub fn player_jump(state: State<'_, AppState>, index: usize) {
    state.player.send(Command::Jump(index));
}

#[tauri::command]
pub fn player_remove(state: State<'_, AppState>, index: usize) {
    state.player.send(Command::Remove(index));
}

#[tauri::command]
pub fn player_toggle(state: State<'_, AppState>) {
    state.player.send(Command::Toggle);
}

#[tauri::command]
pub fn player_next(state: State<'_, AppState>) {
    state.player.send(Command::Next);
}

#[tauri::command]
pub fn player_prev(state: State<'_, AppState>) {
    state.player.send(Command::Prev);
}

#[tauri::command]
pub fn player_seek(state: State<'_, AppState>, seconds: f64) {
    state.player.send(Command::Seek(seconds));
}

#[tauri::command]
pub fn player_stop(state: State<'_, AppState>) {
    state.player.send(Command::Stop);
}

/// Sets the volume live; `persist` (sent when the slider is released) also saves it.
#[tauri::command]
pub fn player_set_volume(app: AppHandle, state: State<'_, AppState>, volume: f32, persist: bool) -> Reply<()> {
    state.player.send(Command::SetVolume(volume));
    if persist {
        let mut settings = state.settings.lock().expect("settings lock");
        settings.volume = volume.clamp(0.0, 1.0);
        settings::save(&app, &settings)?;
    }
    Ok(())
}

/// The colour theme; saved on its own, without checking the server like Save does.
#[tauri::command]
pub fn set_theme(app: AppHandle, state: State<'_, AppState>, theme: Theme) -> Reply<()> {
    let mut settings = state.settings.lock().expect("settings lock");
    settings.theme = theme;
    settings::save(&app, &settings)
}

/// Show the current track on Discord, or not. Saved on its own, like the theme. Off removes the card at once.
#[tauri::command]
pub fn set_discord_presence(app: AppHandle, state: State<'_, AppState>, on: bool) -> Reply<()> {
    if let Some(presence) = &state.presence {
        presence.set_enabled(on);
    }
    let mut settings = state.settings.lock().expect("settings lock");
    settings.discord_presence = on;
    settings::save(&app, &settings)
}

/// Whether this build can show a Discord card (its Discord application id is set).
#[tauri::command]
pub fn discord_presence_available(state: State<'_, AppState>) -> bool {
    state.presence.is_some()
}

/// Shuffle on or off; remembered for the next session.
#[tauri::command]
pub fn player_set_shuffle(app: AppHandle, state: State<'_, AppState>, on: bool) -> Reply<()> {
    state.player.send(Command::SetShuffle(on));
    let mut settings = state.settings.lock().expect("settings lock");
    settings.shuffle = on;
    settings::save(&app, &settings)
}

/// Repeat off, all or one; remembered for the next session.
#[tauri::command]
pub fn player_set_repeat(app: AppHandle, state: State<'_, AppState>, mode: RepeatMode) -> Reply<()> {
    state.player.send(Command::SetRepeat(mode));
    let mut settings = state.settings.lock().expect("settings lock");
    settings.repeat = mode;
    settings::save(&app, &settings)
}
