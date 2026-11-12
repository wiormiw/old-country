//! Calls into the desktop core. The UI never does HTTP: every server request and every player
//! control is a Tauri command, and player state arrives as an event.

use serde::Serialize;
use serde::de::DeserializeOwned;
use shared::{
    AppError, DownloadStatus, Downloads, ImportReport, Paged, PlayerState, Playlist, PlaylistDownloadReport, PlaylistSummary, QueueItem, RepeatMode,
    ScanStatus, Settings, Track, TrackDetail, YouTubeResult,
};
use wasm_bindgen::prelude::*;

#[wasm_bindgen]
extern "C" {
    #[wasm_bindgen(js_namespace = ["window", "__TAURI__", "core"], catch)]
    async fn invoke(command: &str, args: JsValue) -> Result<JsValue, JsValue>;

    #[wasm_bindgen(js_namespace = ["window", "__TAURI__", "event"])]
    async fn listen(event: &str, handler: &Closure<dyn FnMut(JsValue)>) -> JsValue;
}

thread_local! {
    /// How many server requests are running, for the progress bar. Set up by `track_requests`.
    static IN_FLIGHT: std::cell::Cell<Option<leptos::prelude::RwSignal<u32>>> = const { std::cell::Cell::new(None) };
}

/// Counts server requests in `counter` from now on (the app shows a progress bar while it's > 0).
pub fn track_requests(counter: leptos::prelude::RwSignal<u32>) {
    IN_FLIGHT.with(|cell| cell.set(Some(counter)));
}

/// Counts one request for as long as it lives, so early returns and errors are counted down too.
struct InFlight(Option<leptos::prelude::RwSignal<u32>>);

impl InFlight {
    fn start(command: &str) -> Self {
        // Player controls and local settings never wait on the server; leave them out.
        let local = command.starts_with("player_") || matches!(command, "get_settings" | "logs_folder" | "open_logs_folder");
        let counter = if local { None } else { IN_FLIGHT.with(std::cell::Cell::get) };
        if let Some(counter) = counter {
            leptos::prelude::Update::update(&counter, |n| *n += 1);
        }
        Self(counter)
    }
}

impl Drop for InFlight {
    fn drop(&mut self) {
        if let Some(counter) = self.0 {
            leptos::prelude::Update::try_update(&counter, |n| *n = n.saturating_sub(1));
        }
    }
}

/// Invokes `command` with `args` (a struct whose camelCase fields match the command's parameters).
async fn call<T: DeserializeOwned>(command: &str, args: impl Serialize) -> Result<T, AppError> {
    let _in_flight = InFlight::start(command);
    let args = args
        .serialize(&serde_wasm_bindgen::Serializer::json_compatible())
        .map_err(|e| AppError::new("ui.bad_request", e.to_string()))?;
    match invoke(command, args).await {
        Ok(value) => serde_wasm_bindgen::from_value(value).map_err(|e| AppError::new("ui.bad_reply", e.to_string())),
        Err(error) => Err(serde_wasm_bindgen::from_value::<AppError>(error.clone()).unwrap_or_else(|_| {
            AppError::new("ui.command_failed", error.as_string().unwrap_or_else(|| format!("{command} failed")))
        })),
    }
}

#[derive(Serialize)]
struct NoArgs {}

macro_rules! args {
    ($($field:ident : $ty:ty = $value:expr),* $(,)?) => {{
        #[derive(Serialize)]
        #[serde(rename_all = "camelCase")]
        struct Args { $($field: $ty),* }
        Args { $($field: $value),* }
    }};
}

// --- Settings ---

pub async fn get_settings() -> Result<Settings, AppError> {
    call("get_settings", NoArgs {}).await
}

pub async fn save_settings(settings: Settings) -> Result<Settings, AppError> {
    call("save_settings", args!(settings: Settings = settings)).await
}

pub async fn check_server(url: String) -> Result<(), AppError> {
    call("check_server", args!(url: String = url)).await
}

// --- Library ---

pub async fn list_tracks(page: u32, page_size: u32, search: Option<String>) -> Result<Paged<Track>, AppError> {
    call("list_tracks", args!(page: u32 = page, page_size: u32 = page_size, search: Option<String> = search)).await
}

pub async fn get_track(id: String) -> Result<TrackDetail, AppError> {
    call("get_track", args!(id: String = id)).await
}

pub async fn logs_folder() -> Result<String, AppError> {
    call("logs_folder", NoArgs {}).await
}

pub async fn open_logs_folder() -> Result<(), AppError> {
    call("open_logs_folder", NoArgs {}).await
}

pub async fn scan_library() -> Result<ScanStatus, AppError> {
    call("scan_library", NoArgs {}).await
}

pub async fn scan_status() -> Result<ScanStatus, AppError> {
    call("scan_status", NoArgs {}).await
}

// --- Playlists ---

pub async fn list_playlists() -> Result<Vec<PlaylistSummary>, AppError> {
    call("list_playlists", NoArgs {}).await
}

pub async fn get_playlist(id: String) -> Result<Playlist, AppError> {
    call("get_playlist", args!(id: String = id)).await
}

pub async fn create_playlist(name: String, description: Option<String>) -> Result<PlaylistSummary, AppError> {
    call("create_playlist", args!(name: String = name, description: Option<String> = description)).await
}

pub async fn update_playlist(id: String, name: String, description: Option<String>) -> Result<PlaylistSummary, AppError> {
    call("update_playlist", args!(id: String = id, name: String = name, description: Option<String> = description)).await
}

pub async fn delete_playlist(id: String) -> Result<(), AppError> {
    call("delete_playlist", args!(id: String = id)).await
}

pub async fn add_playlist_tracks(id: String, track_ids: Vec<String>) -> Result<PlaylistSummary, AppError> {
    call("add_playlist_tracks", args!(id: String = id, track_ids: Vec<String> = track_ids)).await
}

pub async fn remove_playlist_entry(id: String, position: u32) -> Result<(), AppError> {
    call("remove_playlist_entry", args!(id: String = id, position: u32 = position)).await
}

pub async fn move_playlist_entry(id: String, from: u32, to: u32) -> Result<(), AppError> {
    call("move_playlist_entry", args!(id: String = id, from: u32 = from, to: u32 = to)).await
}

// --- YouTube Music ---

pub async fn youtube_search(query: String, limit: u32) -> Result<Vec<YouTubeResult>, AppError> {
    call("youtube_search", args!(query: String = query, limit: u32 = limit)).await
}

pub async fn youtube_download(video_ids: Vec<String>) -> Result<Vec<DownloadStatus>, AppError> {
    call("youtube_download", args!(video_ids: Vec<String> = video_ids)).await
}

pub async fn youtube_downloads() -> Result<Downloads, AppError> {
    call("youtube_downloads", NoArgs {}).await
}

pub async fn download_playlist(id: String, retry_skipped: bool) -> Result<PlaylistDownloadReport, AppError> {
    call("download_playlist", args!(id: String = id, retry_skipped: bool = retry_skipped)).await
}

pub async fn sync_playlist(id: String) -> Result<ImportReport, AppError> {
    call("sync_playlist", args!(id: String = id)).await
}

pub async fn youtube_import(playlist: String) -> Result<ImportReport, AppError> {
    call("youtube_import", args!(playlist: String = playlist)).await
}

// --- Player ---

pub async fn player_state() -> Result<PlayerState, AppError> {
    call("player_state", NoArgs {}).await
}

pub async fn play(items: Vec<QueueItem>, start: usize) {
    fire("player_play", args!(items: Vec<QueueItem> = items, start: usize = start)).await;
}

pub async fn enqueue(items: Vec<QueueItem>) {
    fire("player_enqueue", args!(items: Vec<QueueItem> = items)).await;
}

pub async fn jump(index: usize) {
    fire("player_jump", args!(index: usize = index)).await;
}

pub async fn remove_from_queue(index: usize) {
    fire("player_remove", args!(index: usize = index)).await;
}

pub async fn toggle() {
    fire("player_toggle", NoArgs {}).await;
}

pub async fn next() {
    fire("player_next", NoArgs {}).await;
}

pub async fn prev() {
    fire("player_prev", NoArgs {}).await;
}

pub async fn seek(seconds: f64) {
    fire("player_seek", args!(seconds: f64 = seconds)).await;
}

pub async fn set_volume(volume: f32, persist: bool) {
    fire("player_set_volume", args!(volume: f32 = volume, persist: bool = persist)).await;
}

pub async fn set_shuffle(on: bool) {
    fire("player_set_shuffle", args!(on: bool = on)).await;
}

pub async fn set_repeat(mode: RepeatMode) {
    fire("player_set_repeat", args!(mode: RepeatMode = mode)).await;
}

/// Player controls report back through state events, so their replies carry nothing to show.
async fn fire(command: &str, args: impl Serialize) {
    if let Err(error) = call::<()>(command, args).await {
        leptos::logging::warn!("{command}: {}", error.message);
    }
}

/// Calls `handler` with every player state the core pushes. Listens for the app's lifetime.
pub fn on_player_state(handler: impl Fn(PlayerState) + 'static) {
    let closure = Closure::<dyn FnMut(JsValue)>::new(move |event: JsValue| {
        let payload = js_sys::Reflect::get(&event, &JsValue::from_str("payload")).unwrap_or(JsValue::NULL);
        match serde_wasm_bindgen::from_value::<PlayerState>(payload) {
            Ok(state) => handler(state),
            Err(error) => leptos::logging::warn!("unreadable player state: {error}"),
        }
    });
    leptos::task::spawn_local(async move {
        listen(shared::PLAYER_STATE_EVENT, &closure).await;
        // The listener must live as long as the app.
        closure.forget();
    });
}
