//! old-country desktop core: settings, the server API client, and the native player, exposed to
//! the Leptos UI as Tauri commands and events.

pub mod api;
pub mod commands;
pub mod player;
pub mod settings;

use std::sync::{Arc, Mutex, OnceLock, RwLock};
use std::time::Duration;

use shared::PLAYER_STATE_EVENT;
use tauri::{Emitter, Manager};
use tauri_plugin_log::{RotationStrategy, Target, TargetKind};

use crate::api::ApiClient;
use crate::commands::AppState;
use crate::player::{PlayerHandle, StartSettings};
use crate::player::cache::AudioCache;
use crate::player::media_keys::MediaKeys;

/// The main window's native handle, which the Windows media controls attach to.
#[cfg(target_os = "windows")]
fn window_handle(app: &tauri::App) -> Option<*mut std::ffi::c_void> {
    let window = app.get_webview_window("main")?;
    window.hwnd().map(|hwnd| hwnd.0).map_err(|e| log::warn!("no window handle: {e}")).ok()
}

#[cfg(not(target_os = "windows"))]
fn window_handle(_app: &tauri::App) -> Option<*mut std::ffi::c_void> {
    None
}

/// Log file name (without extension) in the app's log folder.
pub const LOG_FILE: &str = "old-country";

pub fn run() {
    // Logs go to the terminal and to a file in the app's log folder
    // (Linux: ~/.local/share/dev.wiormiw.oldcountry/logs/old-country.log), kept to 3 x 5 MB.
    let logging = tauri_plugin_log::Builder::new()
        .clear_targets()
        .targets([
            Target::new(TargetKind::Stdout),
            Target::new(TargetKind::LogDir { file_name: Some(LOG_FILE.into()) }),
        ])
        .level(log::LevelFilter::Info)
        .level_for("old_country_lib", log::LevelFilter::Debug)
        .max_file_size(5 * 1024 * 1024)
        .rotation_strategy(RotationStrategy::KeepSome(3))
        .build();

    let mut builder = tauri::Builder::default();
    // One running copy, like other music players: launching it again brings the open window to
    // the front instead of starting a second player. Release builds only, so `cargo tauri dev`
    // still starts while the installed app is open. It must be the first plugin registered.
    if !cfg!(debug_assertions) {
        builder = builder.plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            log::info!("another launch: showing the open window instead");
            if let Some(window) = app.get_webview_window("main") {
                let _ = window.unminimize();
                let _ = window.show();
                let _ = window.set_focus();
            }
        }));
    }

    builder
        .plugin(logging)
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_store::Builder::default().build())
        .setup(|app| {
            let settings = settings::load(app.handle());
            let server = Arc::new(RwLock::new(settings.server_url.clone()));
            let http = reqwest::Client::builder()
                .connect_timeout(Duration::from_secs(5))
                .user_agent(concat!("old-country-desktop/", env!("CARGO_PKG_VERSION")))
                .build()?;

            let cache_dir = app.path().app_cache_dir()?.join("audio");
            let runtime = tauri::async_runtime::handle().inner().clone();
            let cache = Arc::new(AudioCache::new(cache_dir, http.clone(), runtime)?);
            cache.trim(settings.cache_max_megabytes * 1024 * 1024);

            // Every state change goes to the UI and to the desktop's media controls. The media
            // controls need the player handle to send commands, so they're attached right after.
            let events = app.handle().clone();
            let media_keys: Arc<OnceLock<MediaKeys>> = Arc::new(OnceLock::new());
            let mirror = Arc::clone(&media_keys);
            let start = StartSettings { volume: settings.volume, shuffle: settings.shuffle, repeat: settings.repeat };
            let player = PlayerHandle::spawn(Arc::clone(&cache), Arc::clone(&server), start, move |state| {
                let _ = events.emit(PLAYER_STATE_EVENT, state);
                if let Some(keys) = mirror.get() {
                    keys.update(state);
                }
            });
            if let Some(keys) = MediaKeys::start(player.clone(), window_handle(app)) {
                let _ = media_keys.set(keys);
            }

            #[cfg(debug_assertions)]
            watch_ui_script(app.handle().clone());

            app.manage(AppState {
                api: ApiClient::new(http, Arc::clone(&server)),
                player,
                server,
                settings: Mutex::new(settings),
                cache,
            });
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            commands::get_settings,
            commands::save_settings,
            commands::set_theme,
            commands::check_server,
            commands::list_tracks,
            commands::get_track,
            commands::logs_folder,
            commands::open_logs_folder,
            commands::scan_library,
            commands::scan_status,
            commands::list_playlists,
            commands::get_playlist,
            commands::create_playlist,
            commands::update_playlist,
            commands::delete_playlist,
            commands::add_playlist_tracks,
            commands::remove_playlist_entry,
            commands::move_playlist_entry,
            commands::youtube_search,
            commands::youtube_download,
            commands::youtube_downloads,
            commands::download_playlist,
            commands::sync_playlist,
            commands::youtube_import,
            commands::player_state,
            commands::player_play,
            commands::player_enqueue,
            commands::player_jump,
            commands::player_remove,
            commands::player_toggle,
            commands::player_next,
            commands::player_prev,
            commands::player_seek,
            commands::player_stop,
            commands::player_set_volume,
            commands::player_set_shuffle,
            commands::player_set_repeat,
        ])
        .run(tauri::generate_context!())
        .expect("old-country failed to start");
}

/// Debug builds only: when `OLDCOUNTRY_UI_SCRIPT` names a file, runs its JavaScript in the window
/// each time the file changes. Drives the real UI step by step for screenshots and manual checks
/// (the docs/screenshots in the README are made this way). Not compiled into release builds.
#[cfg(debug_assertions)]
fn watch_ui_script(app: tauri::AppHandle) {
    let Ok(path) = std::env::var("OLDCOUNTRY_UI_SCRIPT") else { return };
    log::info!("running UI scripts from {path} whenever it changes");
    std::thread::spawn(move || {
        let mut last_run = None;
        loop {
            std::thread::sleep(Duration::from_millis(400));
            let Ok(modified) = std::fs::metadata(&path).and_then(|m| m.modified()) else { continue };
            if last_run == Some(modified) {
                continue;
            }
            last_run = Some(modified);
            match (std::fs::read_to_string(&path), app.get_webview_window("main")) {
                (Ok(script), Some(window)) => {
                    if let Err(error) = window.eval(&script) {
                        log::warn!("UI script failed: {error}");
                    }
                }
                _ => log::debug!("UI script or window not ready yet"),
            }
        }
    });
}
