//! Plays the whole library (or the tracks that match a search) as one queue.
//!
//! The first track starts at once. The rest of the library loads in the background, one page at
//! a time, and goes to the player as appends of the same session. A new `Play` or a `Stop` takes
//! a larger session number, and then the loader stops.

use std::time::Instant;

use shared::{AppError, QueueItem, track_queue_item};

use super::{Command, PlayerHandle};
use crate::api::ApiClient;

/// The tracks in one page request. The server accepts 200 at most (`ListTracksRequestValidator`).
pub const LOAD_PAGE_SIZE: u32 = 200;
/// The loader adds no more tracks than this to the queue.
pub const MAX_QUEUE: u32 = 20_000;
/// With shuffle on, the random picks to try. After them the first playable track in page order is used.
pub const RANDOM_TRIES: u32 = 5;

/// Where the loader sends its commands. [`PlayerHandle`] is the real one; tests record them.
pub trait QueueSink {
    fn new_session(&self) -> u64;
    fn session(&self) -> u64;
    fn send(&self, command: Command);
}

impl QueueSink for PlayerHandle {
    fn new_session(&self) -> u64 {
        PlayerHandle::new_session(self)
    }

    fn session(&self) -> u64 {
        PlayerHandle::session(self)
    }

    fn send(&self, command: Command) {
        PlayerHandle::send(self, command);
    }
}

/// The part of the library that is not in the queue yet. [`Rest::load`] adds it.
pub struct Rest {
    search: Option<String>,
    total: u32,
    page_size: u32,
    session: u64,
    started: Instant,
}

/// Starts the first track of the library and returns the rest, to load in the background.
pub async fn play_library(
    api: &ApiClient,
    player: &impl QueueSink,
    search: Option<String>,
    shuffle: bool,
) -> Result<Rest, AppError> {
    start(api, player, search, shuffle, LOAD_PAGE_SIZE).await
}

async fn start(
    api: &ApiClient,
    player: &impl QueueSink,
    search: Option<String>,
    shuffle: bool,
    page_size: u32,
) -> Result<Rest, AppError> {
    let started = Instant::now();
    // Take the number before the requests. A user action during them takes a larger number, so
    // the engine ignores this Play and the loader stops.
    let session = player.new_session();
    let total = api.list_tracks(1, 1, search.as_deref()).await?.total_count;
    if total == 0 {
        return Err(AppError::new("library.empty", "The library has no tracks to play."));
    }
    let Some(first) = first_track(api, search.as_deref(), total, shuffle, page_size).await? else {
        return Err(AppError::new(
            "library.unplayable",
            "No track in the library can play: none is downloaded or on YouTube.",
        ));
    };
    player.send(Command::Play { items: vec![first], start: 0, context: None, session });
    Ok(Rest { search, total, page_size, session, started })
}

/// The pages that the loader reads.
fn page_count(total: u32, page_size: u32) -> u32 {
    total.min(MAX_QUEUE).div_ceil(page_size)
}

/// The track to start with. With shuffle on, a random track of the whole range. Else (or when
/// no random pick can play), the first playable track in the pages that the loader reads.
async fn first_track(
    api: &ApiClient,
    search: Option<&str>,
    total: u32,
    shuffle: bool,
    page_size: u32,
) -> Result<Option<QueueItem>, AppError> {
    if shuffle {
        for _ in 0..RANDOM_TRIES {
            let pick = fastrand::u32(0..total);
            let page = api.list_tracks(pick + 1, 1, search).await?;
            if let Some(item) = page.items.iter().find_map(track_queue_item) {
                return Ok(Some(item));
            }
        }
    }
    for page in 1..=page_count(total, page_size) {
        let found = api.list_tracks(page, page_size, search).await?;
        if let Some(item) = found.items.iter().find_map(track_queue_item) {
            return Ok(Some(item));
        }
    }
    Ok(None)
}

impl Rest {
    /// Appends the library to the queue, one page at a time. It stops when a newer `Play` or a
    /// `Stop` takes a larger session. The first track is appended again; the queue skips it.
    pub async fn load(self, api: &ApiClient, player: &impl QueueSink) {
        let mut appended = 0;
        for page in 1..=page_count(self.total, self.page_size) {
            // The engine sets its session when it processes the Play. Until then it shows an
            // older, smaller number. All numbers come from one rising counter, thus only a
            // larger number means that a newer Play or a Stop replaced this queue.
            if player.session() > self.session {
                log::info!("library load stopped after {appended} tracks: a newer queue replaced it");
                return;
            }
            match api.list_tracks(page, self.page_size, self.search.as_deref()).await {
                Ok(found) => {
                    let items: Vec<QueueItem> = found.items.iter().filter_map(track_queue_item).collect();
                    appended += items.len();
                    if !items.is_empty() {
                        player.send(Command::Append { session: self.session, items });
                    }
                }
                Err(error) => log::warn!("library load: page {page} failed and is skipped: {error}"),
            }
        }
        log::info!(
            "library load: {appended} playable tracks of {} in {:.2?}",
            self.total,
            self.started.elapsed()
        );
    }
}

#[cfg(test)]
mod tests {
    use std::collections::HashMap;
    use std::sync::atomic::{AtomicU64, Ordering};
    use std::sync::{Arc, Mutex, RwLock};

    use shared::{Paged, Track};
    use wiremock::matchers::{method, path};
    use wiremock::{Mock, MockServer, Request, Respond, ResponseTemplate};

    use super::*;

    /// One request to the stub library.
    #[derive(Clone, Debug)]
    struct Asked {
        page: u32,
        page_size: u32,
        search: Option<String>,
    }

    /// A server library: `GET /api/tracks?page=&pageSize=` pages through `tracks`.
    /// It records each request. With `user_plays`, the user starts a queue while the request runs:
    /// it takes a session number from the counter and the engine makes it the current one.
    struct Library {
        tracks: Vec<Track>,
        requests: Arc<Mutex<Vec<Asked>>>,
        user_plays: Option<Arc<Recorder>>,
    }

    impl Respond for Library {
        fn respond(&self, request: &Request) -> ResponseTemplate {
            let query: HashMap<String, String> = request.url.query_pairs().into_owned().collect();
            let page: u32 = query["page"].parse().unwrap();
            let page_size: u32 = query["pageSize"].parse().unwrap();
            self.requests.lock().unwrap().push(Asked { page, page_size, search: query.get("search").cloned() });
            if let Some(player) = &self.user_plays
                && player.session() == 0
            {
                let session = player.new_session();
                player.send(Command::Play { items: Vec::new(), start: 0, context: None, session });
            }
            let total = self.tracks.len() as u32;
            let items = self
                .tracks
                .iter()
                .skip(((page - 1) * page_size) as usize)
                .take(page_size as usize)
                .cloned()
                .collect();
            ResponseTemplate::new(200).set_body_json(Paged {
                items,
                page,
                page_size,
                total_count: total,
                total_pages: total.div_ceil(page_size),
            })
        }
    }

    /// Track `n` is called `t{n}`. A playable track is downloaded; the other has no file and no video.
    fn track(n: usize, playable: bool) -> Track {
        Track {
            id: format!("id{n}"),
            title: format!("t{n}"),
            artist: "Band".into(),
            duration: 60.0,
            is_available_offline: playable,
            codec: None,
            you_tube_video_id: None,
            created_at: "2026-01-01T00:00:00Z".into(),
            download_problem: None,
        }
    }

    fn tracks(count: usize, playable: impl Fn(usize) -> bool) -> Vec<Track> {
        (1..=count).map(|n| track(n, playable(n))).collect()
    }

    struct Stub {
        server: MockServer,
        requests: Arc<Mutex<Vec<Asked>>>,
    }

    impl Stub {
        async fn start(tracks: Vec<Track>) -> Self {
            Self::with_user(tracks, None).await
        }

        async fn with_user(tracks: Vec<Track>, user_plays: Option<Arc<Recorder>>) -> Self {
            let server = MockServer::start().await;
            let requests = Arc::new(Mutex::new(Vec::new()));
            Mock::given(method("GET"))
                .and(path("/api/tracks"))
                .respond_with(Library { tracks, requests: Arc::clone(&requests), user_plays })
                .mount(&server)
                .await;
            Self { server, requests }
        }

        /// The requests since the last call.
        fn requests(&self) -> Vec<Asked> {
            std::mem::take(&mut *self.requests.lock().unwrap())
        }

        fn api(&self) -> ApiClient {
            ApiClient::new(reqwest::Client::new(), Arc::new(RwLock::new(self.server.uri())))
        }
    }

    #[derive(Debug, PartialEq)]
    enum Sent {
        Play { titles: Vec<String>, session: u64 },
        Append { titles: Vec<String>, session: u64 },
        Other,
    }

    fn titles(items: &[QueueItem]) -> Vec<String> {
        items.iter().map(|item| item.title.clone()).collect()
    }

    /// Records the commands. Like the engine, it takes the session of a `Play` as current when it
    /// is not older than the current one, unless `play_is_slow` is set (the engine did not process
    /// the `Play` yet).
    #[derive(Default)]
    struct Recorder {
        counter: AtomicU64,
        current: AtomicU64,
        sent: Mutex<Vec<Sent>>,
        play_is_slow: bool,
        /// After the first append, a new `Play` takes a larger session.
        replaced_after_first_append: bool,
    }

    impl QueueSink for Recorder {
        fn new_session(&self) -> u64 {
            self.counter.fetch_add(1, Ordering::SeqCst) + 1
        }

        fn session(&self) -> u64 {
            self.current.load(Ordering::SeqCst)
        }

        fn send(&self, command: Command) {
            let sent = match command {
                Command::Play { items, session, .. } => {
                    if !self.play_is_slow && session >= self.session() {
                        self.current.store(session, Ordering::SeqCst);
                    }
                    Sent::Play { titles: titles(&items), session }
                }
                Command::Append { session, items } => {
                    if self.replaced_after_first_append {
                        let newer = self.new_session();
                        self.current.store(newer, Ordering::SeqCst);
                    }
                    Sent::Append { titles: titles(&items), session }
                }
                _ => Sent::Other,
            };
            self.sent.lock().unwrap().push(sent);
        }
    }

    impl Recorder {
        fn sent(&self) -> Vec<Sent> {
            std::mem::take(&mut *self.sent.lock().unwrap())
        }

        fn first_play(&self) -> Vec<String> {
            match self.sent.lock().unwrap().first() {
                Some(Sent::Play { titles, .. }) => titles.clone(),
                other => panic!("the first command is not Play: {other:?}"),
            }
        }
    }

    fn strings(names: &[&str]) -> Vec<String> {
        names.iter().map(|name| (*name).to_owned()).collect()
    }

    async fn play_all(api: &ApiClient, player: &Recorder, search: Option<String>, shuffle: bool, page_size: u32) {
        let rest = start(api, player, search, shuffle, page_size).await.unwrap();
        rest.load(api, player).await;
    }

    #[tokio::test]
    async fn shuffle_off_starts_with_the_first_track_then_appends_the_rest() {
        let stub = Stub::start(tracks(5, |_| true)).await;
        let api = stub.api();
        let player = Recorder::default();

        play_all(&api, &player, None, false, 2).await;

        assert_eq!(
            player.sent(),
            vec![
                Sent::Play { titles: strings(&["t1"]), session: 1 },
                Sent::Append { titles: strings(&["t1", "t2"]), session: 1 },
                Sent::Append { titles: strings(&["t3", "t4"]), session: 1 },
                Sent::Append { titles: strings(&["t5"]), session: 1 },
            ]
        );
    }

    #[tokio::test]
    async fn random_first_track_comes_from_the_whole_range() {
        let stub = Stub::start(tracks(1000, |_| true)).await;
        let api = stub.api();
        let mut picks = Vec::new();
        for _ in 0..200 {
            let player = Recorder::default();
            start(&api, &player, None, true, 2).await.unwrap();
            let title = player.first_play().remove(0);
            picks.push(title.trim_start_matches('t').parse::<usize>().unwrap());
        }

        assert!(picks.iter().any(|&n| n <= 100), "no pick in the first tenth");
        assert!(picks.iter().any(|&n| n > 900), "no pick in the last tenth");
    }

    #[tokio::test]
    async fn unplayable_random_pick_is_retried() {
        let stub = Stub::start(tracks(1000, |n| n % 2 == 0)).await;
        let api = stub.api();
        let mut retried = false;
        for _ in 0..20 {
            let player = Recorder::default();
            start(&api, &player, None, true, 2).await.unwrap();
            let title = player.first_play().remove(0);
            let n: usize = title.trim_start_matches('t').parse().unwrap();
            assert_eq!(n % 2, 0, "{title} cannot play");

            // The first request counts the tracks. The random picks ask for one track each.
            let picks: Vec<u32> =
                stub.requests().iter().skip(1).take_while(|asked| asked.page_size == 1).map(|asked| asked.page).collect();
            let (last, before) = picks.split_last().unwrap();
            assert!(before.iter().all(|page| page % 2 == 1), "a playable pick was not used: {picks:?}");
            assert!(last % 2 == 0 || picks.len() == RANDOM_TRIES as usize, "the picks stopped early: {picks:?}");
            retried |= picks.len() > 1;
        }
        assert!(retried, "no unplayable pick was tried again");
    }

    #[tokio::test]
    async fn shuffle_off_skips_unplayable_tracks_to_find_the_first() {
        let stub = Stub::start(tracks(5, |n| n == 4)).await;
        let api = stub.api();
        let player = Recorder::default();

        start(&api, &player, None, false, 2).await.unwrap();

        assert_eq!(player.first_play(), strings(&["t4"]));
    }

    #[tokio::test]
    async fn library_without_playable_tracks_reports_an_error() {
        let stub = Stub::start(tracks(5, |_| false)).await;
        let api = stub.api();
        for shuffle in [false, true] {
            let player = Recorder::default();

            let error = start(&api, &player, None, shuffle, 2).await.err().unwrap();

            assert_eq!(error.code, "library.unplayable");
            assert_eq!(player.sent(), vec![]);
        }
    }

    #[tokio::test]
    async fn empty_library_reports_an_error() {
        let stub = Stub::start(Vec::new()).await;
        let player = Recorder::default();

        let error = start(&stub.api(), &player, None, false, 2).await.err().unwrap();

        assert_eq!(error.code, "library.empty");
        assert_eq!(player.sent(), vec![]);
    }

    #[tokio::test]
    async fn loader_stops_when_the_session_changes() {
        let stub = Stub::start(tracks(5, |_| true)).await;
        let api = stub.api();
        let player = Recorder { replaced_after_first_append: true, ..Default::default() };

        play_all(&api, &player, None, false, 2).await;

        assert_eq!(
            player.sent(),
            vec![
                Sent::Play { titles: strings(&["t1"]), session: 1 },
                Sent::Append { titles: strings(&["t1", "t2"]), session: 1 },
            ]
        );
    }

    #[tokio::test]
    async fn a_user_play_during_the_start_wins() {
        let player = Arc::new(Recorder::default());
        let stub = Stub::with_user(tracks(5, |_| true), Some(Arc::clone(&player))).await;
        let api = stub.api();

        play_all(&api, &player, None, false, 2).await;

        // The loader took session 1 first; the user's Play took 2 during the count request.
        // The engine ignores the late Play 1, and the loader appends nothing.
        assert_eq!(
            player.sent(),
            vec![
                Sent::Play { titles: vec![], session: 2 },
                Sent::Play { titles: strings(&["t1"]), session: 1 },
            ]
        );
        assert_eq!(player.session(), 2);
    }

    #[tokio::test]
    async fn loader_keeps_appending_while_the_play_is_not_processed() {
        // The engine still reports the older, smaller session: the Play is in its inbox.
        let stub = Stub::start(tracks(5, |_| true)).await;
        let api = stub.api();
        let player = Recorder { play_is_slow: true, ..Default::default() };

        play_all(&api, &player, None, false, 2).await;

        let appends = player.sent().into_iter().filter(|sent| matches!(sent, Sent::Append { .. })).count();
        assert_eq!(appends, 3);
    }

    #[tokio::test]
    async fn search_is_passed_to_every_request() {
        let stub = Stub::start(tracks(5, |_| true)).await;
        let api = stub.api();
        let player = Recorder::default();

        play_all(&api, &player, Some("band".into()), true, 2).await;

        let requests = stub.requests();
        assert!(requests.len() >= 5, "only {} requests", requests.len());
        assert!(requests.iter().all(|asked| asked.search.as_deref() == Some("band")), "{requests:?}");
    }

    #[tokio::test]
    async fn the_load_is_capped_at_max_queue() {
        let total = MAX_QUEUE as usize + 5;
        let stub = Stub::start(tracks(total, |_| true)).await;
        let api = stub.api();
        let player = Recorder::default();

        play_all(&api, &player, None, false, LOAD_PAGE_SIZE).await;

        let appended: usize = player
            .sent()
            .iter()
            .map(|sent| match sent {
                Sent::Append { titles, .. } => titles.len(),
                _ => 0,
            })
            .sum();
        assert_eq!(appended, MAX_QUEUE as usize);
    }
}
