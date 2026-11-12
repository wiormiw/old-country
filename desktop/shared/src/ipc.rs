//! What the UI and the desktop core exchange. Commands return `Result<T, AppError>`; the player
//! pushes [`PlayerState`] on the [`PLAYER_STATE_EVENT`] event.

use std::collections::BTreeMap;

use serde::{Deserialize, Serialize};

use crate::api::ProblemDetails;

/// Event the core emits whenever playback state changes, and a few times a second while playing.
pub const PLAYER_STATE_EVENT: &str = "player://state";

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

/// One entry in the play queue, with what the now-playing bar shows.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct QueueItem {
    pub audio: AudioRef,
    pub title: String,
    pub artist: String,
    /// Seconds, when known before playing.
    pub duration: Option<f64>,
}

/// Everything the UI needs to draw the player. The core is the only source of truth.
#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlayerState {
    pub queue: Vec<QueueItem>,
    /// Index into `queue` of the current item.
    pub index: Option<usize>,
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

/// Settings the user controls, persisted by the core.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Settings {
    /// Base URL of the old-country API, e.g. `http://192.168.1.10:5000`.
    pub server_url: String,
    /// Largest size of the local audio cache, in megabytes.
    pub cache_max_megabytes: u64,
    pub volume: f32,
}

impl Default for Settings {
    fn default() -> Self {
        Self { server_url: String::new(), cache_max_megabytes: 2_048, volume: 0.8 }
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
