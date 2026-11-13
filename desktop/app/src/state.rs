//! App-wide reactive state, provided through Leptos context.

use leptos::prelude::*;
use shared::{AudioRef, ImportReport, PlayerState, PlaylistEntry, QueueItem, Theme, Track, YouTubeResult};

use crate::ipc;

/// The latest player state pushed by the core.
#[derive(Clone, Copy)]
pub struct Player(pub RwSignal<PlayerState>);

/// How many server requests are running; the progress bar shows while it's above zero.
#[derive(Clone, Copy)]
pub struct Requests(pub RwSignal<u32>);

/// A short message shown at the bottom of the screen (errors, confirmations).
#[derive(Clone, Copy)]
pub struct Notice(pub RwSignal<Option<(bool, String)>>);

impl Notice {
    pub fn error(&self, message: impl Into<String>) {
        self.0.set(Some((true, message.into())));
    }

    pub fn info(&self, message: impl Into<String>) {
        self.0.set(Some((false, message.into())));
    }
}

/// The colour theme the user picked. The page's `data-theme` attribute follows it.
#[derive(Clone, Copy)]
pub struct ThemeChoice(pub RwSignal<Theme>);

/// Where the theme is cached for `theme.js`, which applies it before the app has loaded.
const THEME_KEY: &str = "old-country.theme";

impl ThemeChoice {
    /// Switches theme now and remembers it.
    pub fn set(&self, theme: Theme) {
        self.0.set(theme);
        leptos::task::spawn_local(async move {
            let _ = ipc::set_theme(theme).await;
        });
    }
}

fn apply_theme(theme: Theme) {
    if let Some(root) = document().document_element() {
        let _ = match theme.attribute() {
            Some(value) => root.set_attribute("data-theme", value),
            None => root.remove_attribute("data-theme"),
        };
    }
    if let Ok(Some(storage)) = window().local_storage() {
        let _ = storage.set_item(THEME_KEY, theme.attribute().unwrap_or("system"));
    }
}

fn provide_theme() {
    // theme.js already applied the cached choice; start from it, then take the saved setting.
    let initial = document()
        .document_element()
        .and_then(|root| root.get_attribute("data-theme"))
        .map_or(Theme::System, |value| Theme::from_attribute(&value));
    let theme = RwSignal::new(initial);
    Effect::new(move |_| apply_theme(theme.get()));
    leptos::task::spawn_local(async move {
        if let Ok(settings) = ipc::get_settings().await {
            theme.set(settings.theme);
        }
    });
    provide_context(ThemeChoice(theme));
}

pub fn use_theme() -> ThemeChoice {
    expect_context::<ThemeChoice>()
}

pub fn provide_app_state() {
    provide_theme();
    let player = RwSignal::new(PlayerState::default());
    provide_context(Player(player));
    provide_context(Notice(RwSignal::new(None)));
    let requests = RwSignal::new(0);
    ipc::track_requests(requests);
    provide_context(Requests(requests));

    ipc::on_player_state(move |state| player.set(state));
    leptos::task::spawn_local(async move {
        if let Ok(state) = ipc::player_state().await {
            player.set(state);
        }
    });
}

pub fn use_player() -> Player {
    expect_context::<Player>()
}

pub fn use_requests() -> Requests {
    expect_context::<Requests>()
}

pub fn use_notice() -> Notice {
    expect_context::<Notice>()
}

// --- Building queue items ---

/// A library track plays from its local file, or streams from YouTube when it only exists there.
pub fn track_item(track: &Track) -> QueueItem {
    let audio = match (&track.you_tube_video_id, track.is_available_offline) {
        (Some(video_id), false) => AudioRef::YouTube { video_id: video_id.clone() },
        _ => AudioRef::Track { id: track.id.clone() },
    };
    QueueItem {
        audio,
        title: track.title.clone(),
        artist: track.artist.clone(),
        duration: Some(track.duration),
        video_id: track.you_tube_video_id.clone(),
    }
}

pub fn entry_item(entry: &PlaylistEntry) -> QueueItem {
    let audio = match (&entry.you_tube_video_id, entry.is_available_offline) {
        (Some(video_id), false) => AudioRef::YouTube { video_id: video_id.clone() },
        _ => AudioRef::Track { id: entry.track_id.clone() },
    };
    QueueItem {
        audio,
        title: entry.title.clone(),
        artist: entry.artist.clone(),
        duration: Some(entry.duration),
        video_id: entry.you_tube_video_id.clone(),
    }
}

/// A search result plays the library's local copy when there is one, else streams from YouTube.
pub fn youtube_item(result: &YouTubeResult) -> QueueItem {
    let audio = match (&result.track_id, result.is_available_offline) {
        (Some(id), true) => AudioRef::Track { id: id.clone() },
        _ => AudioRef::YouTube { video_id: result.video_id.clone() },
    };
    QueueItem {
        audio,
        title: result.title.clone(),
        artist: result.artist.clone().unwrap_or_else(|| "YouTube".into()),
        duration: result.duration,
        video_id: Some(result.video_id.clone()),
    }
}

// --- Download problems ---

/// A short label for why YouTube refused a song's download (the server's `DownloadRefusal`).
pub fn problem_label(reason: &str) -> &'static str {
    match reason {
        "PremiumOnly" => "Premium only",
        "Private" => "Private",
        "Unavailable" => "Removed",
        "AgeRestricted" => "Age restricted",
        "RegionBlocked" => "Not in your country",
        _ => "Download failed",
    }
}

/// Refusals that won't change by simply trying again; "Download all" skips these.
pub fn is_lasting_problem(reason: &str) -> bool {
    reason != "Failed"
}

/// The same label for a failed download job's error code (`youtube.premium_only`, ...).
pub fn error_code_label(code: &str) -> Option<&'static str> {
    Some(match code {
        "youtube.premium_only" => "Premium only",
        "youtube.private" => "Private",
        "youtube.unavailable" => "Removed",
        "youtube.age_restricted" => "Age restricted",
        "youtube.region_blocked" => "Not in your country",
        "youtube.rate_limited" => "YouTube asked to slow down",
        "download.gave_up" => "Gave up after 3 tries",
        _ => return None,
    })
}

// --- YouTube sync ---

/// What an import or a sync did, in one sentence.
pub fn import_message(report: &ImportReport) -> String {
    let skipped = if report.skipped > 0 { format!(", {} skipped", report.skipped) } else { String::new() };
    match (report.is_new, report.added) {
        (Some(false), Some(0)) => format!("\"{}\" is already up to date with YouTube.", report.name),
        (Some(false), Some(added)) => format!(
            "Updated \"{}\" from YouTube: {added} new song{} added ({} in total){skipped}.",
            report.name,
            if added == 1 { "" } else { "s" },
            report.entries,
        ),
        _ => format!(
            "Imported \"{}\": {} songs, {} new to the library{skipped}.",
            report.name, report.entries, report.tracks_created,
        ),
    }
}

/// "just now", "5 min ago", "3 h ago", "2 days ago" for an ISO timestamp.
pub fn time_ago(iso: &str) -> String {
    let then = js_sys::Date::new(&wasm_bindgen::JsValue::from_str(iso)).get_time();
    if then.is_nan() {
        return iso.to_owned();
    }
    let minutes = ((js_sys::Date::now() - then) / 60_000.0).max(0.0) as u64;
    match minutes {
        0 => "just now".to_owned(),
        1..=59 => format!("{minutes} min ago"),
        60..=1_439 => format!("{} h ago", minutes / 60),
        _ => match minutes / 1_440 {
            1 => "yesterday".to_owned(),
            days => format!("{days} days ago"),
        },
    }
}
