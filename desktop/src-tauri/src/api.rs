//! The old-country server's HTTP API, called from the core so the UI never does HTTP itself.
//! Every failure becomes an [`AppError`]: the server's problem details when it answered, or a
//! desktop code (`server.not_configured`, `server.unreachable`) when it couldn't.

use reqwest::{Method, Response, Url};
use serde::Serialize;
use serde::de::DeserializeOwned;
use serde_json::json;
use shared::{
    AppError, DownloadStatus, Downloads, ImportReport, Paged, Playlist, PlaylistDownloadReport, PlaylistSummary, ProblemDetails,
    ScanStatus, Track,
    TrackDetail, YouTubeResult,
};

use crate::player::ServerUrl;

pub struct ApiClient {
    http: reqwest::Client,
    server: ServerUrl,
}

impl ApiClient {
    pub fn new(http: reqwest::Client, server: ServerUrl) -> Self {
        Self { http, server }
    }

    /// Checks that `base_url` is an old-country server answering its health endpoint.
    pub async fn check_server(&self, base_url: &str) -> Result<(), AppError> {
        let url = format!("{}/health", base_url.trim_end_matches('/'));
        let response = self.http.get(&url).send().await.map_err(unreachable)?;
        if response.status().is_success() {
            Ok(())
        } else {
            Err(AppError::new(
                "server.unhealthy",
                format!("{base_url} answered {} instead of being healthy.", response.status()),
            ))
        }
    }

    // --- Tracks ---

    pub async fn list_tracks(&self, page: u32, page_size: u32, search: Option<&str>) -> Result<Paged<Track>, AppError> {
        let mut query = vec![("page", page.to_string()), ("pageSize", page_size.to_string())];
        if let Some(search) = search.map(str::trim).filter(|s| !s.is_empty()) {
            query.push(("search", search.to_owned()));
        }
        self.get_query("/api/tracks", &query).await
    }

    pub async fn get_track(&self, id: &str) -> Result<TrackDetail, AppError> {
        self.get(&format!("/api/tracks/{id}")).await
    }

    pub async fn scan_library(&self) -> Result<ScanStatus, AppError> {
        self.send(Method::POST, "/api/tracks/scans", None::<()>).await
    }

    pub async fn scan_status(&self) -> Result<ScanStatus, AppError> {
        self.get("/api/tracks/scans/latest").await
    }

    // --- Playlists ---

    pub async fn list_playlists(&self) -> Result<Vec<PlaylistSummary>, AppError> {
        self.get("/api/playlists").await
    }

    pub async fn get_playlist(&self, id: &str) -> Result<Playlist, AppError> {
        self.get(&format!("/api/playlists/{id}")).await
    }

    pub async fn create_playlist(&self, name: &str, description: Option<&str>) -> Result<PlaylistSummary, AppError> {
        self.send(Method::POST, "/api/playlists", Some(json!({ "name": name, "description": description }))).await
    }

    pub async fn update_playlist(
        &self,
        id: &str,
        name: &str,
        description: Option<&str>,
    ) -> Result<PlaylistSummary, AppError> {
        self.send(Method::PUT, &format!("/api/playlists/{id}"), Some(json!({ "name": name, "description": description })))
            .await
    }

    pub async fn delete_playlist(&self, id: &str) -> Result<(), AppError> {
        self.send_empty(Method::DELETE, &format!("/api/playlists/{id}"), None::<()>).await
    }

    pub async fn add_playlist_tracks(&self, id: &str, track_ids: &[String]) -> Result<PlaylistSummary, AppError> {
        self.send(Method::POST, &format!("/api/playlists/{id}/tracks"), Some(json!({ "trackIds": track_ids }))).await
    }

    pub async fn remove_playlist_entry(&self, id: &str, position: u32) -> Result<(), AppError> {
        self.send_empty(Method::DELETE, &format!("/api/playlists/{id}/tracks/{position}"), None::<()>).await
    }

    pub async fn move_playlist_entry(&self, id: &str, from: u32, to: u32) -> Result<(), AppError> {
        self.send_empty(Method::POST, &format!("/api/playlists/{id}/tracks/move"), Some(json!({ "from": from, "to": to })))
            .await
    }

    // --- YouTube Music ---

    pub async fn youtube_search(&self, query: &str, limit: u32) -> Result<Vec<YouTubeResult>, AppError> {
        self.get_query("/api/youtube-music/search", &[("query", query.to_owned()), ("limit", limit.to_string())]).await
    }

    pub async fn youtube_download(&self, video_ids: &[String]) -> Result<Vec<DownloadStatus>, AppError> {
        self.send(Method::POST, "/api/youtube-music/downloads", Some(json!({ "videoIds": video_ids }))).await
    }

    pub async fn youtube_downloads(&self) -> Result<Downloads, AppError> {
        self.get("/api/youtube-music/downloads").await
    }

    /// Queues every song of a playlist that isn't downloaded yet (and, with `retry_skipped`, the
    /// ones YouTube refused before).
    pub async fn download_playlist(&self, id: &str, retry_skipped: bool) -> Result<PlaylistDownloadReport, AppError> {
        self.send(Method::POST, &format!("/api/youtube-music/playlists/{id}/downloads"), Some(json!({ "retrySkipped": retry_skipped })))
            .await
    }

    /// Syncs an imported playlist with YouTube Music now.
    pub async fn sync_playlist(&self, id: &str) -> Result<ImportReport, AppError> {
        self.send(Method::POST, &format!("/api/youtube-music/playlists/{id}/sync"), None::<()>).await
    }

    pub async fn youtube_import(&self, playlist: &str) -> Result<ImportReport, AppError> {
        self.send(Method::POST, "/api/youtube-music/imports", Some(json!({ "playlist": playlist }))).await
    }

    // --- Plumbing ---

    fn url(&self, path: &str, query: &[(&str, String)]) -> Result<Url, AppError> {
        let base = self.server.read().expect("server url lock").clone();
        if base.is_empty() {
            return Err(AppError::new("server.not_configured", "Set the server address in Settings first."));
        }
        let joined = format!("{}{path}", base.trim_end_matches('/'));
        Url::parse_with_params(&joined, query)
            .map_err(|e| AppError::new("server.bad_address", format!("'{base}' isn't a valid server address: {e}")))
    }

    async fn get<T: DeserializeOwned>(&self, path: &str) -> Result<T, AppError> {
        self.get_query(path, &[]).await
    }

    async fn get_query<T: DeserializeOwned>(&self, path: &str, query: &[(&str, String)]) -> Result<T, AppError> {
        let response = self.http.get(self.url(path, query)?).send().await.map_err(unreachable)?;
        json_body(checked(response).await?).await
    }

    async fn send<T: DeserializeOwned>(&self, method: Method, path: &str, body: Option<impl Serialize>) -> Result<T, AppError> {
        json_body(self.request(method, path, body).await?).await
    }

    async fn send_empty(&self, method: Method, path: &str, body: Option<impl Serialize>) -> Result<(), AppError> {
        self.request(method, path, body).await.map(drop)
    }

    async fn request(&self, method: Method, path: &str, body: Option<impl Serialize>) -> Result<Response, AppError> {
        let mut request = self.http.request(method, self.url(path, &[])?);
        if let Some(body) = body {
            request = request.json(&body);
        }
        checked(request.send().await.map_err(unreachable)?).await
    }
}

/// Passes successful responses through; turns the rest into the server's problem details.
async fn checked(response: Response) -> Result<Response, AppError> {
    if response.status().is_success() {
        return Ok(response);
    }
    let status = response.status().as_u16();
    let problem = response.json::<ProblemDetails>().await.unwrap_or_default();
    Err(AppError::from_problem(status, problem))
}

async fn json_body<T: DeserializeOwned>(response: Response) -> Result<T, AppError> {
    response
        .json::<T>()
        .await
        .map_err(|e| AppError::new("server.bad_response", format!("The server's answer couldn't be read: {e}")))
}

pub fn unreachable(error: reqwest::Error) -> AppError {
    let reason = if error.is_timeout() { "it didn't answer in time".to_owned() } else { error.to_string() };
    AppError::new("server.unreachable", format!("The server couldn't be reached: {reason}"))
}

#[cfg(test)]
mod tests {
    use std::sync::{Arc, RwLock};

    use wiremock::matchers::{body_json, method, path, query_param};
    use wiremock::{Mock, MockServer, ResponseTemplate};

    use super::*;

    fn client(base: &str) -> ApiClient {
        ApiClient::new(reqwest::Client::new(), Arc::new(RwLock::new(base.to_owned())))
    }

    #[tokio::test]
    async fn queries_are_encoded_and_bodies_parsed() {
        let server = MockServer::start().await;
        Mock::given(method("GET"))
            .and(path("/api/youtube-music/search"))
            .and(query_param("query", "rick & morty"))
            .and(query_param("limit", "5"))
            .respond_with(ResponseTemplate::new(200).set_body_raw(
                r#"[{"videoId":"dQw4w9WgXcQ","title":"T","duration":"00:03:33","isAvailableOffline":false}]"#,
                "application/json",
            ))
            .mount(&server)
            .await;

        let results = client(&server.uri()).youtube_search("rick & morty", 5).await.unwrap();

        assert_eq!(results[0].duration, Some(213.0));
    }

    #[tokio::test]
    async fn problem_details_become_app_errors_with_field_messages() {
        let server = MockServer::start().await;
        Mock::given(method("POST"))
            .and(path("/api/playlists"))
            .and(body_json(serde_json::json!({ "name": "", "description": null })))
            .respond_with(ResponseTemplate::new(400).set_body_raw(
                r#"{"title":"One or more validation errors occurred.","status":400,"code":"validation","errors":{"name":["Give the playlist a name."]}}"#,
                "application/problem+json",
            ))
            .mount(&server)
            .await;

        let error = client(&server.uri()).create_playlist("", None).await.unwrap_err();

        assert_eq!(error.code, "validation");
        assert_eq!(error.status, Some(400));
        assert_eq!(error.message, "Give the playlist a name.");
        assert_eq!(error.field_errors["name"], vec!["Give the playlist a name."]);
    }

    #[tokio::test]
    async fn no_content_answers_are_success() {
        let server = MockServer::start().await;
        Mock::given(method("DELETE"))
            .and(path("/api/playlists/p1/tracks/2"))
            .respond_with(ResponseTemplate::new(204))
            .mount(&server)
            .await;

        client(&server.uri()).remove_playlist_entry("p1", 2).await.unwrap();
    }

    #[tokio::test]
    async fn an_unset_server_says_to_configure_it() {
        let error = client("").list_playlists().await.unwrap_err();
        assert_eq!(error.code, "server.not_configured");
    }

    #[tokio::test]
    async fn an_unreachable_server_is_reported_as_such() {
        // Port 9 (discard) is closed on a normal machine: the connection is refused.
        let error = client("http://127.0.0.1:9").list_playlists().await.unwrap_err();
        assert_eq!(error.code, "server.unreachable");
    }

    #[tokio::test]
    async fn the_health_check_rejects_a_server_that_is_not_healthy() {
        let server = MockServer::start().await;
        Mock::given(method("GET")).and(path("/health")).respond_with(ResponseTemplate::new(503)).mount(&server).await;

        let error = client("").check_server(&server.uri()).await.unwrap_err();

        assert_eq!(error.code, "server.unhealthy");
    }
}
