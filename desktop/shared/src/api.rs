//! The server's JSON. Mirrors the response records in `backend/src/OldCountry.Api/Features`.
//! Ids are kept as strings (GUIDs and YouTube ids pass straight through), timestamps as the
//! server's ISO 8601 text.

use std::collections::BTreeMap;

use serde::{Deserialize, Serialize};

use crate::timespan;

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Track {
    pub id: String,
    pub title: String,
    pub artist: String,
    #[serde(with = "timespan::seconds")]
    pub duration: f64,
    pub is_available_offline: bool,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub codec: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub you_tube_video_id: Option<String>,
    pub created_at: String,
    /// Why YouTube refused the last download (`PremiumOnly`, `Private`, `Unavailable`,
    /// `AgeRestricted`, `RegionBlocked`, `Failed`), until one succeeds.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub download_problem: Option<String>,
}

/// A playlist a track appears in.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlaylistRef {
    pub id: String,
    pub name: String,
}

/// Everything about one track, for its detail page.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TrackDetail {
    pub track: Track,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub file_name: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub size_bytes: Option<u64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub bitrate_kbps: Option<u32>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub sample_rate_hz: Option<u32>,
    pub playlists: Vec<PlaylistRef>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Paged<T> {
    pub items: Vec<T>,
    pub page: u32,
    pub page_size: u32,
    pub total_count: u32,
    pub total_pages: u32,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub enum ScanTrigger {
    Startup,
    Scheduled,
    Manual,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ScanReport {
    pub trigger: ScanTrigger,
    pub files_seen: u32,
    pub added: u32,
    pub already_known: u32,
    pub unreadable: u32,
    pub missing: u32,
    pub started_at: String,
    pub finished_at: String,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ScanStatus {
    pub is_running: bool,
    pub is_queued: bool,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub last_scan: Option<ScanReport>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlaylistSummary {
    pub id: String,
    pub name: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub description: Option<String>,
    pub track_count: u32,
    pub created_at: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub updated_at: Option<String>,
    /// The YouTube Music playlist it was imported from and syncs with, if any.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub you_tube_list_id: Option<String>,
    /// When it last synced with that playlist.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub you_tube_synced_at: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlaylistEntry {
    pub position: u32,
    pub track_id: String,
    pub title: String,
    pub artist: String,
    #[serde(with = "timespan::seconds")]
    pub duration: f64,
    pub is_available_offline: bool,
    /// Set for tracks that exist on YouTube Music (imported or downloaded from there).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub you_tube_video_id: Option<String>,
    pub added_at: String,
    /// Why YouTube refused the last download, until one succeeds (see [`Track::download_problem`]).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub download_problem: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Playlist {
    pub id: String,
    pub name: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub description: Option<String>,
    pub entries: Vec<PlaylistEntry>,
    #[serde(with = "timespan::seconds")]
    pub total_duration: f64,
    pub created_at: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub updated_at: Option<String>,
    /// The YouTube Music playlist it was imported from and syncs with, if any.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub you_tube_list_id: Option<String>,
    /// When it last synced with that playlist.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub you_tube_synced_at: Option<String>,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct YouTubeResult {
    pub video_id: String,
    pub title: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub artist: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none", with = "timespan::optional")]
    pub duration: Option<f64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub thumbnail_url: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub track_id: Option<String>,
    pub is_available_offline: bool,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub enum DownloadState {
    Queued,
    Running,
    Completed,
    Failed,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DownloadStatus {
    pub job_id: String,
    pub video_id: String,
    /// The song's title, when it's a library track.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub title: Option<String>,
    pub state: DownloadState,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub track_id: Option<String>,
    /// Why it failed, as a stable code (`youtube.premium_only`, `youtube.private`, ...).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub error_code: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
    #[serde(default)]
    pub attempts: u32,
    pub queued_at: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub finished_at: Option<String>,
}

/// Recent downloads, and whether the queue is resting because YouTube asked us to slow down.
#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Downloads {
    pub jobs: Vec<DownloadStatus>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub paused_until: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub pause_reason: Option<String>,
}

/// What asking for a playlist's downloads did.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlaylistDownloadReport {
    pub queued: u32,
    pub already_downloaded: u32,
    pub already_queued: u32,
    /// Left out: YouTube refused them before for a lasting reason (Premium only, private, ...).
    pub skipped: u32,
    pub not_on_you_tube: u32,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ImportReport {
    pub playlist_id: String,
    pub name: String,
    /// `Some(false)` when the playlist had been imported before and this synced it. `None` from
    /// servers older than sync, which always created a new playlist.
    #[serde(default)]
    pub is_new: Option<bool>,
    /// Entries this import added (all for a new playlist, only songs new on YouTube for a sync).
    #[serde(default)]
    pub added: Option<u32>,
    /// Entries in the playlist now.
    pub entries: u32,
    pub tracks_created: u32,
    pub tracks_existing: u32,
    pub skipped: u32,
}

/// RFC 9457 problem details as the server writes them, with its stable `code` extension.
#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ProblemDetails {
    #[serde(default)]
    pub title: Option<String>,
    #[serde(default)]
    pub status: Option<u16>,
    #[serde(default)]
    pub detail: Option<String>,
    #[serde(default)]
    pub code: Option<String>,
    #[serde(default)]
    pub errors: BTreeMap<String, Vec<String>>,
}

#[cfg(test)]
mod tests {
    use super::*;

    // Bodies as the server returns them (see backend/src/OldCountry.Api/OldCountry.Api.http).

    #[test]
    fn a_track_page_parses() {
        let json = r#"{"items":[{"id":"0193f4a2-0000-7000-8000-000000000001","title":"Dust Road","artist":"Old Country Band",
            "duration":"00:03:35","isAvailableOffline":true,"codec":"Flac","createdAt":"2026-11-12T06:33:07.123+00:00"}],
            "page":1,"pageSize":50,"totalCount":1,"totalPages":1}"#;

        let page: Paged<Track> = serde_json::from_str(json).unwrap();

        let track = &page.items[0];
        assert_eq!(track.duration, 215.0);
        assert_eq!(track.codec.as_deref(), Some("Flac"));
        assert_eq!(track.you_tube_video_id, None);
        assert_eq!(page.total_pages, 1);
    }

    #[test]
    fn a_search_result_without_optional_fields_parses() {
        let json = r#"{"videoId":"dQw4w9WgXcQ","title":"Never Gonna Give You Up","isAvailableOffline":false}"#;

        let result: YouTubeResult = serde_json::from_str(json).unwrap();

        assert_eq!(result.duration, None);
        assert_eq!(result.artist, None);
    }

    #[test]
    fn a_validation_problem_keeps_its_field_errors() {
        let json = r#"{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.",
            "status":400,"errors":{"title":["Give the track a title."]},"code":"validation","traceId":"00-abc"}"#;

        let problem: ProblemDetails = serde_json::from_str(json).unwrap();

        assert_eq!(problem.code.as_deref(), Some("validation"));
        assert_eq!(problem.errors["title"], vec!["Give the track a title."]);
    }

    #[test]
    fn enums_use_the_servers_names() {
        let status: DownloadStatus = serde_json::from_str(
            r#"{"jobId":"j","videoId":"dQw4w9WgXcQ","state":"Completed","trackId":"t","queuedAt":"x","finishedAt":"y"}"#,
        )
        .unwrap();
        assert_eq!(status.state, DownloadState::Completed);
    }

    #[test]
    fn durations_round_trip_through_timespan_text() {
        let entry = PlaylistEntry {
            position: 0,
            track_id: "t".into(),
            title: "T".into(),
            artist: "A".into(),
            duration: 4_000.0,
            is_available_offline: false,
            you_tube_video_id: None,
            added_at: "x".into(),
            download_problem: None,
        };
        let json = serde_json::to_string(&entry).unwrap();
        assert!(json.contains(r#""duration":"01:06:40""#));
        assert_eq!(serde_json::from_str::<PlaylistEntry>(&json).unwrap(), entry);
    }

    #[test]
    fn import_reports_say_whether_they_synced_and_older_servers_still_parse() {
        let sync: ImportReport = serde_json::from_str(
            r#"{"playlistId":"p","name":"Mix","isNew":false,"entries":68,"added":3,"tracksCreated":3,"tracksExisting":0,"skipped":0}"#,
        )
        .unwrap();
        assert_eq!((sync.is_new, sync.added, sync.entries), (Some(false), Some(3), 68));

        let older: ImportReport = serde_json::from_str(
            r#"{"playlistId":"p","name":"Mix","entries":65,"tracksCreated":65,"tracksExisting":0,"skipped":1}"#,
        )
        .unwrap();
        assert_eq!((older.is_new, older.added), (None, None));
    }
}
