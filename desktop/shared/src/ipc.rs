//! What the UI and the desktop core exchange. Commands return `Result<T, AppError>`; the player
//! pushes [`PlayerState`] on the [`PLAYER_STATE_EVENT`] event.

use std::collections::BTreeMap;

use serde::{Deserialize, Serialize};

use crate::api::ProblemDetails;

/// Event the core emits whenever playback state changes, and a few times a second while playing.
pub const PLAYER_STATE_EVENT: &str = "player://state";

/// Event the core emits when the server says something changed (see `src-tauri/src/live.rs`).
pub const LIBRARY_CHANGES_EVENT: &str = "server://changes";

/// Event the core emits with the server's news (a download finished, songs arrived from YouTube).
pub const NEWS_EVENT: &str = "server://news";

/// News from the server worth showing the person, as the hub's `Announced` sends it. The server
/// decides the wording; the ids say what it's about.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Announcement {
    /// `Downloaded`, `DownloadFailed` or `NewFromYouTube` (kept as text, so new kinds still show).
    pub kind: String,
    pub message: String,
    #[serde(default)]
    pub track_id: Option<String>,
    #[serde(default)]
    pub playlist_id: Option<String>,
}

/// Event the core emits when the live connection to the server comes up or goes down.
pub const LIVE_STATUS_EVENT: &str = "server://live";

/// The live connection to the server's change notifications.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum LiveStatus {
    /// No server set, or not started.
    #[default]
    Off,
    Connecting,
    /// Connected: changes made elsewhere show up on their own.
    Live,
    /// Lost; trying again.
    Reconnecting,
}

/// What changed on the server: a hint to refetch, not the data. Mirrors the server's
/// `LibraryChanges` (the hub's `Changed` message).
#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct LibraryChanges {
    /// Playlists created, changed or deleted (their ids).
    pub playlists: Vec<String>,
    /// Songs added, changed or removed (including becoming downloaded).
    pub tracks: bool,
    /// YouTube download jobs changed.
    pub downloads: bool,
    /// Refetch everything: the server fell behind, or the app was offline and may have missed some.
    pub everything: bool,
}

impl LibraryChanges {
    pub fn everything() -> Self {
        Self { everything: true, ..Self::default() }
    }

    pub fn touches_playlist(&self, id: &str) -> bool {
        self.everything || self.playlists.iter().any(|p| p == id)
    }

    pub fn touches_playlist_list(&self) -> bool {
        self.everything || !self.playlists.is_empty()
    }

    pub fn touches_tracks(&self) -> bool {
        self.everything || self.tracks
    }

    pub fn touches_downloads(&self) -> bool {
        self.everything || self.downloads
    }
}

/// Where a queued item's audio comes from on the server.
#[derive(Clone, Debug, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", rename_all_fields = "camelCase")]
pub enum AudioRef {
    /// A library track: `GET /api/tracks/{id}/audio`.
    Track { id: String },
    /// A YouTube video, fetched and cached by the server: `GET /api/youtube-music/videos/{id}/audio`.
    YouTube { video_id: String },
}

impl AudioRef {
    /// Server path of the audio, relative to the API base URL.
    pub fn path(&self) -> String {
        match self {
            AudioRef::Track { id } => format!("/api/tracks/{id}/audio"),
            AudioRef::YouTube { video_id } => format!("/api/youtube-music/videos/{video_id}/audio"),
        }
    }

    /// Stable file-name-safe key for the local audio cache.
    pub fn cache_key(&self) -> String {
        match self {
            AudioRef::Track { id } => format!("track-{id}"),
            AudioRef::YouTube { video_id } => format!("yt-{video_id}"),
        }
    }
}

/// What happens at the end of a song or of the queue.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum RepeatMode {
    /// Stop after the last song.
    #[default]
    Off,
    /// Start over after the last song (with shuffle: a fresh shuffle, every song once per cycle).
    All,
    /// Replay the current song when it ends. Next still moves on.
    One,
}

impl RepeatMode {
    /// The order the repeat button cycles through.
    pub fn next(self) -> Self {
        match self {
            RepeatMode::Off => RepeatMode::All,
            RepeatMode::All => RepeatMode::One,
            RepeatMode::One => RepeatMode::Off,
        }
    }
}

/// One entry in the play queue, with what the now-playing bar shows.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct QueueItem {
    pub audio: AudioRef,
    pub title: String,
    pub artist: String,
    /// Seconds, when known before playing.
    pub duration: Option<f64>,
    /// The YouTube video the song came from, for its thumbnail.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub video_id: Option<String>,
}

/// A YouTube video's thumbnail. The 4:3 picture letterboxes 16:9 videos; crop it to show them.
pub fn thumbnail_url(video_id: &str) -> String {
    format!("https://i.ytimg.com/vi/{video_id}/hqdefault.jpg")
}

/// Everything the UI needs to draw the player. The core is the only source of truth.
#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlayerState {
    pub queue: Vec<QueueItem>,
    /// Index into `queue` of the current item.
    pub index: Option<usize>,
    /// Indices into `queue` in the order they play (shuffled when `shuffle` is on).
    pub order: Vec<usize>,
    pub shuffle: bool,
    pub repeat: RepeatMode,
    pub playing: bool,
    /// Fetching audio before playback can start (or continue after a seek).
    pub buffering: bool,
    pub position: f64,
    pub duration: Option<f64>,
    /// 0.0 to 1.0.
    pub volume: f32,
    /// Why the current item can't play, if it can't.
    pub error: Option<String>,
}

impl PlayerState {
    pub fn current(&self) -> Option<&QueueItem> {
        self.index.and_then(|i| self.queue.get(i))
    }
}

/// The colour theme: the system's choice, or always light or dark.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum Theme {
    #[default]
    System,
    Light,
    Dark,
}

impl Theme {
    /// The value of the page's `data-theme` attribute (none follows the system).
    pub fn attribute(self) -> Option<&'static str> {
        match self {
            Theme::System => None,
            Theme::Light => Some("light"),
            Theme::Dark => Some("dark"),
        }
    }

    pub fn from_attribute(value: &str) -> Self {
        match value {
            "light" => Theme::Light,
            "dark" => Theme::Dark,
            _ => Theme::System,
        }
    }
}

/// Settings the user controls, persisted by the core.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Settings {
    /// Base URL of the old-country API, e.g. `http://192.168.1.10:5000`.
    pub server_url: String,
    /// Largest size of the local audio cache, in megabytes.
    pub cache_max_megabytes: u64,
    pub volume: f32,
    #[serde(default)]
    pub shuffle: bool,
    #[serde(default)]
    pub repeat: RepeatMode,
    #[serde(default)]
    pub theme: Theme,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            server_url: String::new(),
            cache_max_megabytes: 2_048,
            volume: 0.8,
            shuffle: false,
            repeat: RepeatMode::Off,
            theme: Theme::System,
        }
    }
}

/// Every failure a command can report, in one shape the UI can show.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AppError {
    /// Stable code: the server's (`track.not_found`, `validation`, `youtube.failed`) or the
    /// desktop's own (`server.unreachable`, `server.not_configured`, `player.unavailable`).
    pub code: String,
    pub message: String,
    pub status: Option<u16>,
    /// Field name → messages, for validation failures.
    #[serde(default)]
    pub field_errors: BTreeMap<String, Vec<String>>,
}

impl AppError {
    pub fn new(code: impl Into<String>, message: impl Into<String>) -> Self {
        Self { code: code.into(), message: message.into(), status: None, field_errors: BTreeMap::new() }
    }

    /// From the server's problem details, preferring its most specific message.
    pub fn from_problem(status: u16, problem: ProblemDetails) -> Self {
        let message = problem
            .errors
            .values()
            .flatten()
            .next()
            .cloned()
            .or(problem.detail)
            .or(problem.title)
            .unwrap_or_else(|| format!("The server answered {status}."));
        Self {
            code: problem.code.unwrap_or_else(|| format!("http.{status}")),
            message,
            status: Some(status),
            field_errors: problem.errors,
        }
    }
}

impl std::fmt::Display for AppError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.message)
    }
}

impl std::error::Error for AppError {}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn audio_refs_map_to_server_paths_and_cache_keys() {
        let track = AudioRef::Track { id: "abc".into() };
        let video = AudioRef::YouTube { video_id: "dQw4w9WgXcQ".into() };
        assert_eq!(track.path(), "/api/tracks/abc/audio");
        assert_eq!(video.path(), "/api/youtube-music/videos/dQw4w9WgXcQ/audio");
        assert_eq!(video.cache_key(), "yt-dQw4w9WgXcQ");
    }

    #[test]
    fn audio_refs_serialise_with_a_kind_tag() {
        let json = serde_json::to_string(&AudioRef::YouTube { video_id: "v".into() }).unwrap();
        assert_eq!(json, r#"{"kind":"youTube","videoId":"v"}"#);
    }

    #[test]
    fn problem_details_prefer_the_first_field_message() {
        let mut problem = ProblemDetails { status: Some(400), code: Some("validation".into()), ..Default::default() };
        problem.errors.insert("name".into(), vec!["Give the playlist a name.".into()]);

        let error = AppError::from_problem(400, problem);

        assert_eq!(error.message, "Give the playlist a name.");
        assert_eq!(error.code, "validation");
    }

    #[test]
    fn problems_without_a_code_get_one_from_the_status() {
        let error = AppError::from_problem(502, ProblemDetails { detail: Some("YouTube refused".into()), ..Default::default() });
        assert_eq!(error.code, "http.502");
        assert_eq!(error.message, "YouTube refused");
    }
}
