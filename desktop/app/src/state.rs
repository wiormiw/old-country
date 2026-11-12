//! App-wide reactive state, provided through Leptos context.

use leptos::prelude::*;
use shared::{AudioRef, PlayerState, PlaylistEntry, QueueItem, Track, YouTubeResult};

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

pub fn provide_app_state() {
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
    QueueItem { audio, title: track.title.clone(), artist: track.artist.clone(), duration: Some(track.duration) }
}

pub fn entry_item(entry: &PlaylistEntry) -> QueueItem {
    let audio = match (&entry.you_tube_video_id, entry.is_available_offline) {
        (Some(video_id), false) => AudioRef::YouTube { video_id: video_id.clone() },
        _ => AudioRef::Track { id: entry.track_id.clone() },
    };
    QueueItem { audio, title: entry.title.clone(), artist: entry.artist.clone(), duration: Some(entry.duration) }
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
