//! Discord Rich Presence: a "Listening to old-country" card on the Discord profile, like Spotify's.
//! It talks to the Discord app on this computer over its local socket. No server is involved, so
//! the pictures must be public: the YouTube thumbnail, or the `logo` asset of the Discord application.

use std::sync::mpsc::{self, Receiver, RecvTimeoutError, Sender};
use std::thread;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use discord_rich_presence::activity::{Activity, ActivityType, Assets, Button, StatusDisplayType, Timestamps};
use discord_rich_presence::{DiscordIpc, DiscordIpcClient};
use shared::PlayerState;

/// The application in the Discord Developer Portal. Its name ("old-country") is the "Listening to"
/// name, and it holds the `logo` asset. The id is public. Empty turns the feature off.
pub const DISCORD_APPLICATION_ID: &str = "1570994694681272370";

/// The art asset of the Discord application, for tracks without a public picture.
const LOGO_ASSET: &str = "logo";

/// Discord refuses longer text fields.
const FIELD_MAX: usize = 128;

/// A start time that moves less than this is playback that goes on, not a seek.
const SEEK_SLACK_MS: i64 = 2_000;

/// When Discord is not running, try again after this time.
const RETRY: Duration = Duration::from_secs(15);

/// What the card shows. The `Activity` of the crate borrows its text, so the thread builds it from this.
#[derive(Clone, Debug, PartialEq)]
pub struct Card {
    pub details: String,
    pub details_url: Option<String>,
    pub state: String,
    pub large_image: String,
    pub large_text: String,
    /// Unix time in milliseconds when the track started (now − position).
    pub start_ms: i64,
    /// When it ends. `None` when the duration is not known: then Discord shows no progress bar.
    pub end_ms: Option<i64>,
    /// Label and URL.
    pub button: Option<(String, String)>,
}

/// The card for this player state at `now_ms`. `None` when nothing plays: paused or stopped.
pub fn card_for(state: &PlayerState, now_ms: i64) -> Option<Card> {
    // Buffering (a first fetch, a seek) keeps the card. Only a real pause or stop removes it.
    if !state.playing && !state.buffering {
        return None;
    }
    let item = state.current()?;
    let start_ms = now_ms - seconds_to_ms(state.position);
    let end_ms = state.duration.or(item.duration).filter(|d| *d > 0.0).map(|d| start_ms + seconds_to_ms(d));
    let youtube = item.video_id.as_deref().map(|id| format!("https://music.youtube.com/watch?v={id}"));
    Some(Card {
        details: field(&item.title, "Unknown title"),
        details_url: youtube.clone(),
        state: field(&item.artist, "Unknown artist"),
        large_image: item.video_id.as_deref().map_or_else(|| LOGO_ASSET.to_owned(), shared::thumbnail_url),
        large_text: field(&state.context.as_ref().map_or_else(|| "Library".to_owned(), |name| format!("Playlist: {name}")), "Library"),
        start_ms,
        end_ms,
        button: youtube.map(|url| ("Play on YouTube Music".to_owned(), url)),
    })
}

/// True when Discord must get the new card. Playback that goes on is not a change, because Discord
/// moves the progress bar by itself. Discord allows about 5 updates in 20 seconds.
pub fn changed(last: Option<&Card>, next: Option<&Card>) -> bool {
    match (last, next) {
        (None, None) => false,
        (Some(a), Some(b)) => {
            let moved = |x: Option<i64>, y: Option<i64>| match (x, y) {
                (Some(x), Some(y)) => (x - y).abs() > SEEK_SLACK_MS,
                (x, y) => x.is_some() != y.is_some(),
            };
            a.details != b.details
                || a.details_url != b.details_url
                || a.state != b.state
                || a.large_image != b.large_image
                || a.large_text != b.large_text
                || a.button != b.button
                || moved(Some(a.start_ms), Some(b.start_ms))
                || moved(a.end_ms, b.end_ms)
        }
        _ => true,
    }
}

fn seconds_to_ms(seconds: f64) -> i64 {
    (seconds.max(0.0) * 1000.0).round() as i64
}

/// A text field that Discord accepts: 2 to 128 characters.
fn field(text: &str, fallback: &str) -> String {
    let text = text.trim();
    let text = if text.is_empty() { fallback } else { text };
    let mut clipped: String = text.chars().take(FIELD_MAX).collect();
    if clipped.chars().count() < 2 {
        // A blank Braille pattern: invisible, but Discord counts it.
        clipped.push('\u{2800}');
    }
    clipped
}

enum Message {
    State(Box<PlayerState>),
    Enabled(bool),
}

/// The handle to the presence thread. Cheap to clone.
#[derive(Clone)]
pub struct Presence {
    messages: Sender<Message>,
}

impl Presence {
    /// Starts the thread. `None` when the Application ID is not set.
    pub fn start(application_id: &str, enabled: bool) -> Option<Self> {
        if application_id.is_empty() {
            log::info!("Discord presence is off: no Discord application id");
            return None;
        }
        let (messages, inbox) = mpsc::channel();
        let id = application_id.to_owned();
        thread::Builder::new()
            .name("discord-presence".into())
            .spawn(move || run(&id, enabled, &inbox))
            .map_err(|e| log::warn!("Discord presence is off: {e}"))
            .ok()?;
        Some(Self { messages })
    }

    /// Gives the thread the newest player state. It decides itself if Discord needs an update.
    pub fn update(&self, state: &PlayerState) {
        let _ = self.messages.send(Message::State(Box::new(state.clone())));
    }

    /// The setting "Show what I'm listening to on Discord". Off removes the card at once.
    pub fn set_enabled(&self, enabled: bool) {
        let _ = self.messages.send(Message::Enabled(enabled));
    }
}

fn now_ms() -> i64 {
    SystemTime::now().duration_since(UNIX_EPOCH).map_or(0, |d| d.as_millis() as i64)
}

/// The thread: keeps the newest state, connects to Discord when there is something to show, and
/// sends a card only on a real change. When Discord is not running or closes, it tries again later.
fn run(application_id: &str, mut enabled: bool, inbox: &Receiver<Message>) {
    let mut latest: Option<PlayerState> = None;
    let mut client: Option<DiscordIpcClient> = None;
    // What Discord shows now. The outer `None`: not known (not connected, or after an error).
    let mut shown: Option<Option<Card>> = None;
    let mut retry_at = Instant::now();

    loop {
        // Wait for a message, but wake for a retry.
        match inbox.recv_timeout(Duration::from_secs(1)) {
            Ok(message) => apply(message, &mut latest, &mut enabled),
            Err(RecvTimeoutError::Timeout) => {}
            Err(RecvTimeoutError::Disconnected) => return,
        }
        // Keep only the newest of the waiting messages.
        while let Ok(message) = inbox.try_recv() {
            apply(message, &mut latest, &mut enabled);
        }

        let wanted = if enabled { latest.as_ref().and_then(|state| card_for(state, now_ms())) } else { None };
        if client.is_none() {
            // Connect only when there is something to show, so a closed Discord costs nothing.
            if wanted.is_none() || Instant::now() < retry_at {
                continue;
            }
            let mut fresh = DiscordIpcClient::new(application_id);
            match fresh.connect() {
                Ok(()) => {
                    log::info!("Discord presence connected");
                    client = Some(fresh);
                    shown = None;
                }
                Err(error) => {
                    log::debug!("Discord is not available: {error}");
                    retry_at = Instant::now() + RETRY;
                    continue;
                }
            }
        }

        let Some(discord) = client.as_mut() else { continue };
        let needed = match &shown {
            None => true,
            Some(card) => changed(card.as_ref(), wanted.as_ref()),
        };
        if !needed {
            continue;
        }
        let sent = match &wanted {
            Some(card) => discord.set_activity(activity(card)),
            None => discord.clear_activity(),
        };
        match sent {
            Ok(()) => shown = Some(wanted),
            Err(error) => {
                // Discord closed. Drop the connection, and connect again later.
                log::info!("Discord presence disconnected: {error}");
                client = None;
                shown = None;
                retry_at = Instant::now() + RETRY;
            }
        }
    }
}

fn apply(message: Message, latest: &mut Option<PlayerState>, enabled: &mut bool) {
    match message {
        Message::State(state) => *latest = Some(*state),
        Message::Enabled(on) => *enabled = on,
    }
}

fn activity(card: &Card) -> Activity<'_> {
    let mut timestamps = Timestamps::new().start(card.start_ms);
    if let Some(end) = card.end_ms {
        timestamps = timestamps.end(end);
    }
    let mut activity = Activity::new()
        .activity_type(ActivityType::Listening)
        .status_display_type(StatusDisplayType::Details)
        .details(card.details.as_str())
        .state(card.state.as_str())
        .assets(Assets::new().large_image(card.large_image.as_str()).large_text(card.large_text.as_str()))
        .timestamps(timestamps);
    if let Some(url) = &card.details_url {
        activity = activity.details_url(url.as_str());
    }
    if let Some((label, url)) = &card.button {
        activity = activity.buttons(vec![Button::new(label.as_str(), url.as_str())]);
    }
    activity
}

#[cfg(test)]
mod tests {
    use super::{Card, card_for, changed};
    use shared::{AudioRef, PlayerState, QueueItem};

    const NOW: i64 = 1_800_000_000_000;

    fn playing(video_id: Option<&str>, context: Option<&str>) -> PlayerState {
        let item = QueueItem {
            audio: match video_id {
                Some(id) => AudioRef::YouTube { video_id: id.to_owned() },
                None => AudioRef::Track { id: "00000000-0000-0000-0000-000000000001".to_owned() },
            },
            title: "Dust Road".to_owned(),
            artist: "The Band".to_owned(),
            duration: Some(200.0),
            video_id: video_id.map(str::to_owned),
        };
        PlayerState {
            queue: vec![item],
            index: Some(0),
            order: vec![0],
            playing: true,
            position: 30.0,
            duration: Some(200.0),
            context: context.map(str::to_owned),
            ..Default::default()
        }
    }

    #[test]
    fn a_youtube_track_shows_its_thumbnail_and_links_to_youtube_music() {
        let card = card_for(&playing(Some("dQw4w9WgXcQ"), Some("Mix")), NOW).expect("a card");
        assert_eq!(card.details, "Dust Road");
        assert_eq!(card.state, "The Band");
        assert_eq!(card.large_image, shared::thumbnail_url("dQw4w9WgXcQ"));
        assert_eq!(card.details_url.as_deref(), Some("https://music.youtube.com/watch?v=dQw4w9WgXcQ"));
        assert_eq!(
            card.button,
            Some(("Play on YouTube Music".to_owned(), "https://music.youtube.com/watch?v=dQw4w9WgXcQ".to_owned()))
        );
        assert_eq!(card.large_text, "Playlist: Mix");
    }

    #[test]
    fn a_local_track_shows_the_logo_and_no_button() {
        let card = card_for(&playing(None, None), NOW).expect("a card");
        assert_eq!(card.large_image, "logo");
        assert_eq!(card.button, None);
        assert_eq!(card.details_url, None);
        assert_eq!(card.large_text, "Library");
    }

    #[test]
    fn the_timestamps_make_the_progress_bar() {
        let card = card_for(&playing(None, None), NOW).expect("a card");
        assert_eq!(card.start_ms, NOW - 30_000);
        assert_eq!(card.end_ms, Some(NOW - 30_000 + 200_000));
    }

    #[test]
    fn a_track_without_a_duration_has_no_end() {
        let mut state = playing(None, None);
        state.duration = None;
        state.queue[0].duration = None;
        assert_eq!(card_for(&state, NOW).expect("a card").end_ms, None);
    }

    #[test]
    fn paused_or_stopped_shows_no_card_but_buffering_does() {
        let mut state = playing(None, None);
        state.playing = false;
        assert!(card_for(&state, NOW).is_none());
        state.buffering = true;
        assert!(card_for(&state, NOW).is_some());
        assert!(card_for(&PlayerState::default(), NOW).is_none());
    }

    #[test]
    fn text_fits_the_limits_of_discord() {
        let mut state = playing(None, None);
        state.queue[0].title = "x".repeat(300);
        state.queue[0].artist = "A".to_owned();
        let card = card_for(&state, NOW).expect("a card");
        assert_eq!(card.details.chars().count(), 128);
        assert_eq!(card.state.chars().count(), 2);
        state.queue[0].artist = "  ".to_owned();
        assert_eq!(card_for(&state, NOW).expect("a card").state, "Unknown artist");
    }

    #[test]
    fn playback_that_goes_on_is_not_a_change_but_a_seek_is() {
        let at = |now: i64, position: f64| {
            let mut state = playing(None, None);
            state.position = position;
            card_for(&state, now).expect("a card")
        };
        let first = at(NOW, 30.0);
        // 10 s later, 10 s further: the same start time.
        assert!(!changed(Some(&first), Some(&at(NOW + 10_000, 40.25))));
        // A seek of 60 s moves the start time.
        assert!(changed(Some(&first), Some(&at(NOW + 1_000, 91.0))));
    }

    #[test]
    fn a_new_track_a_pause_and_a_new_playlist_are_changes() {
        let card: Card = card_for(&playing(None, None), NOW).expect("a card");
        assert!(changed(Some(&card), None));
        assert!(changed(None, Some(&card)));
        assert!(!changed(None, None));
        let other_playlist = card_for(&playing(None, Some("Mix")), NOW).expect("a card");
        assert!(changed(Some(&card), Some(&other_playlist)));
        let other_track = card_for(&playing(Some("dQw4w9WgXcQ"), None), NOW).expect("a card");
        assert!(changed(Some(&card), Some(&other_track)));
    }
}
