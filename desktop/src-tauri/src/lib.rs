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

use crate::api::ApiClient;
use crate::commands::AppState;
use crate::player::{PlayerHandle, StartSettings};
use crate::player::cache::AudioCache;
use crate::player::media_keys::MediaKeys;

pub fn run() {
    env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info,old_country_lib=debug")).init();

    tauri::Builder::default()
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
            if let Some(keys) = MediaKeys::start(player.clone()) {
                let _ = media_keys.set(keys);
            }

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
            commands::check_server,
            commands::list_tracks,
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
