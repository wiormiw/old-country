//! Tauri commands: thin wrappers the UI invokes. Server calls go to [`ApiClient`]; playback goes
//! to the player thread, whose state comes back as events rather than return values.

use std::sync::{Arc, Mutex};

use shared::{
    AppError, DownloadStatus, ImportReport, Paged, PlayerState, Playlist, PlaylistSummary, QueueItem, RepeatMode,
    ScanStatus, Settings, Track, YouTubeResult,
};
use tauri::{AppHandle, State};

use crate::api::ApiClient;
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
}

type Reply<T> = Result<T, AppError>;

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
    Ok(settings)
}

#[tauri::command]
pub async fn check_server(state: State<'_, AppState>, url: String) -> Reply<()> {
    state.api.check_server(&settings::normalize_server_url(&url)).await
}

// --- Library ---

#[tauri::command]
pub async fn list_tracks(state: State<'_, AppState>, page: u32, page_size: u32) -> Reply<Paged<Track>> {
    state.api.list_tracks(page, page_size).await
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
pub async fn youtube_downloads(state: State<'_, AppState>) -> Reply<Vec<DownloadStatus>> {
    state.api.youtube_downloads().await
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
pub fn player_play(state: State<'_, AppState>, items: Vec<QueueItem>, start: usize) {
    state.player.send(Command::Play { items, start });
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
