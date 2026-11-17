//! The player thread. It owns the audio output (rodio's device sink isn't `Send`, so it lives on
//! one thread), the queue and the playback state; everything else talks to it through
//! [`Command`]s and hears back through the `emit` callback.
//!
//! Loading a track (fetch + probe) runs on a short-lived loader thread so the engine stays
//! responsive; a generation number discards loads that finish after the user moved on.

use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::mpsc::{self, RecvTimeoutError, Sender};
use std::sync::{Arc, Mutex, RwLock};
use std::thread;
use std::time::{Duration, Instant};

use shared::{PlayerState, QueueItem, QueueView, RepeatMode};

use super::cache::AudioCache;
use super::decoder::Decoder;
use super::output::Output;
use super::queue::Queue;
use super::source::DecodedSource;

/// How often state is pushed while a track plays (position updates).
const TICK: Duration = Duration::from_millis(250);
/// "Previous" within this many seconds goes to the previous track; later, it restarts this one.
const RESTART_THRESHOLD: f64 = 3.0;
/// Unplayable songs in a row skipped before the player gives up (so one bad video doesn't stop a
/// playlist, and a dead server doesn't spin through the whole queue).
const MAX_SKIPS: u32 = 3;

/// Base URL of the server, shared with the API client and changed from Settings.
pub type ServerUrl = Arc<RwLock<String>>;

pub enum Command {
    /// `context`: the playlist name, when the queue is a playlist.
    /// `session`: from [`PlayerHandle::new_session`]. A background loader appends to this queue
    /// only while its session is the current one.
    Play { items: Vec<QueueItem>, start: usize, context: Option<String>, session: u64 },
    /// Adds items without starting playback. Ignored unless `session` is the current one.
    Append { session: u64, items: Vec<QueueItem> },
    Enqueue(Vec<QueueItem>),
    Jump(usize),
    Remove(usize),
    Toggle,
    Pause,
    Resume,
    Next,
    Prev,
    Seek(f64),
    SetVolume(f32),
    SetShuffle(bool),
    SetRepeat(RepeatMode),
    Stop,
    Loaded { generation: u64, result: Result<DecodedSource, String> },
}

/// Player settings restored from the last session.
#[derive(Clone, Copy, Debug)]
pub struct StartSettings {
    pub volume: f32,
    pub shuffle: bool,
    pub repeat: RepeatMode,
}

/// A handle to the player thread.
#[derive(Clone)]
pub struct PlayerHandle {
    commands: Sender<Command>,
    state: Arc<Mutex<PlayerState>>,
    /// The queue as of `PlayerState::queue_revision`. The engine replaces it only when the
    /// revision changes, so a read does not clone the queue on the player thread.
    queue: Arc<Mutex<Arc<QueueView>>>,
    /// Counts the sessions handed out.
    sessions: Arc<AtomicU64>,
    /// The session of the last `Play` the engine accepted.
    session: Arc<AtomicU64>,
}

/// Takes a new session number from the counter.
fn next_session(counter: &AtomicU64) -> u64 {
    counter.fetch_add(1, Ordering::SeqCst) + 1
}

/// An append belongs to the queue only when its session is the current one.
fn accepts(current: u64, incoming: u64) -> bool {
    current == incoming
}

impl PlayerHandle {
    pub fn spawn(
        cache: Arc<AudioCache>,
        server: ServerUrl,
        start: StartSettings,
        emit: impl Fn(&PlayerState) + Send + 'static,
    ) -> Self {
        let (commands, inbox) = mpsc::channel();
        let volume = start.volume;
        let state = Arc::new(Mutex::new(PlayerState {
            volume,
            shuffle: start.shuffle,
            repeat: start.repeat,
            ..Default::default()
        }));
        let mut queue = Queue::default();
        queue.set_shuffle(start.shuffle);
        queue.set_repeat(start.repeat);
        let view = Arc::new(Mutex::new(Arc::new(queue_view(&queue))));
        let sessions = Arc::new(AtomicU64::new(0));
        let session = Arc::new(AtomicU64::new(0));
        let engine = Engine {
            inbox,
            outbox: commands.clone(),
            cache,
            server,
            published: Arc::clone(&state),
            published_queue: Arc::clone(&view),
            queue,
            generation: 0,
            sessions: Arc::clone(&sessions),
            session: Arc::clone(&session),
            loaded: false,
            want_playing: false,
            buffering: false,
            duration: None,
            volume,
            error: None,
            context: None,
            failures: 0,
        };
        thread::Builder::new()
            .name("player".into())
            .spawn(move || engine.run(emit))
            .expect("spawn the player thread");
        Self { commands, state, queue: view, sessions, session }
    }

    pub fn send(&self, command: Command) {
        // Only fails if the player thread is gone, which ends the app anyway.
        let _ = self.commands.send(command);
    }

    /// A new session number, to pass with `Play`. Each call returns a different one.
    pub fn new_session(&self) -> u64 {
        next_session(&self.sessions)
    }

    /// The session of the last `Play` accepted. It changes on `Stop` too, so a loader that
    /// compares it with its own number stops after Stop.
    pub fn session(&self) -> u64 {
        self.session.load(Ordering::SeqCst)
    }

    pub fn state(&self) -> PlayerState {
        self.state.lock().expect("player state lock").clone()
    }

    /// The full queue. The cursor moves without a new revision, so the index comes from the
    /// last state when that state has the same revision.
    pub fn queue(&self) -> QueueView {
        let view = Arc::clone(&self.queue.lock().expect("player queue lock"));
        let (index, revision) = {
            let state = self.state.lock().expect("player state lock");
            (state.index, state.queue_revision)
        };
        let index = if revision == view.revision { index } else { view.index };
        QueueView { index, ..QueueView::clone(&view) }
    }
}

/// A copy of the queue for the UI.
fn queue_view(queue: &Queue) -> QueueView {
    QueueView { items: queue.items().to_vec(), order: queue.order().to_vec(), index: queue.index(), revision: queue.revision() }
}

struct Engine {
    inbox: mpsc::Receiver<Command>,
    outbox: Sender<Command>,
    cache: Arc<AudioCache>,
    server: ServerUrl,
    published: Arc<Mutex<PlayerState>>,
    published_queue: Arc<Mutex<Arc<QueueView>>>,
    queue: Queue,
    generation: u64,
    sessions: Arc<AtomicU64>,
    session: Arc<AtomicU64>,
    loaded: bool,
    want_playing: bool,
    buffering: bool,
    duration: Option<f64>,
    volume: f32,
    error: Option<String>,
    /// The playlist that the queue came from, if any.
    context: Option<String>,
    /// Songs that failed to load in a row.
    failures: u32,
}

impl Engine {
    fn run(mut self, emit: impl Fn(&PlayerState)) {
        // The sound card stream is open only while playing (see `output`), so a paused or idle
        // app isn't "playing audio" to the desktop. If there's no audio device, the queue keeps
        // working and the error says why nothing plays.
        let mut output = Output::new();
        output.player.set_volume(self.volume as rodio::Float);

        let mut last = PlayerState::default();
        loop {
            match self.inbox.recv_timeout(TICK) {
                Ok(command) => self.handle(command, &mut output),
                Err(RecvTimeoutError::Timeout) => {}
                Err(RecvTimeoutError::Disconnected) => break,
            }
            if self.want_playing
                && let Err(error) = output.open()
            {
                self.error = Some(error);
                self.want_playing = false;
            }
            output.close_when_idle(self.want_playing, Instant::now());
            // A loaded track that has nothing left to play has ended.
            if self.loaded && self.want_playing && output.player.empty() {
                self.queue.advance(true);
                self.start(&mut output);
            }
            let state = self.snapshot(&output);
            // Publish the queue before the state that names its revision. Then a UI that sees
            // the new revision gets the new queue.
            if state.queue_revision != last.queue_revision {
                let view = Arc::new(queue_view(&self.queue));
                *self.published_queue.lock().expect("player queue lock") = view;
            }
            if state != last {
                *self.published.lock().expect("player state lock") = state.clone();
                emit(&state);
                last = state;
            }
        }
    }

    fn handle(&mut self, command: Command, output: &mut Output) {
        match command {
            Command::Play { items, start, context, session } => {
                self.session.store(session, Ordering::SeqCst);
                self.queue.replace(items, start);
                self.context = context;
                self.want_playing = true;
                self.start(output);
            }
            Command::Append { session, items } => {
                if accepts(self.session.load(Ordering::SeqCst), session) {
                    self.queue.append_new(items);
                }
            }
            Command::Enqueue(items) => {
                let idle = self.queue.current().is_none();
                self.queue.enqueue(items);
                if idle {
                    self.want_playing = true;
                    self.start(output);
                }
            }
            Command::Jump(index) => {
                if self.queue.jump(index).is_some() {
                    self.want_playing = true;
                    self.start(output);
                }
            }
            Command::Remove(index) => {
                if self.queue.remove(index) {
                    self.start(output);
                }
            }
            Command::Toggle => {
                if self.want_playing {
                    self.pause(output);
                } else {
                    self.resume(output);
                }
            }
            Command::Pause => self.pause(output),
            Command::Resume => self.resume(output),
            Command::Next => self.next(output),
            Command::Prev => {
                let position = output.player.get_pos().as_secs_f64();
                if position > RESTART_THRESHOLD || self.queue.index() == Some(0) {
                    self.seek(0.0, output);
                } else {
                    self.queue.back();
                    self.start(output);
                }
            }
            Command::Seek(seconds) => self.seek(seconds, output),
            Command::SetShuffle(on) => self.queue.set_shuffle(on),
            Command::SetRepeat(mode) => self.queue.set_repeat(mode),
            Command::SetVolume(volume) => {
                self.volume = volume.clamp(0.0, 1.0);
                output.player.set_volume(self.volume as rodio::Float);
            }
            Command::Stop => {
                let next = next_session(&self.sessions);
                self.session.store(next, Ordering::SeqCst);
                self.queue.clear();
                self.context = None;
                self.want_playing = false;
                self.start(output);
            }
            Command::Loaded { generation, result } => {
                if generation != self.generation {
                    return; // the user moved on while this was loading
                }
                self.buffering = false;
                match result {
                    Ok(source) => {
                        self.failures = 0;
                        self.duration = source.duration().or(self.duration);
                        output.player.append(source);
                        if self.want_playing {
                            output.player.play();
                        }
                        self.loaded = true;
                        self.prefetch_next();
                    }
                    Err(reason) => {
                        let title = self.queue.current().map(|i| i.title.clone()).unwrap_or_default();
                        log::warn!("couldn't play \"{title}\": {reason}");
                        self.failures += 1;
                        let can_skip = self.failures < MAX_SKIPS && self.queue.peek_next().is_some() && self.want_playing;
                        if can_skip {
                            self.queue.advance(false);
                            self.start(output);
                            // After start(), which clears errors: say what was skipped and why.
                            self.error = Some(format!("Skipped \"{title}\": {reason}"));
                        } else {
                            self.error = Some(reason);
                            self.want_playing = false;
                        }
                    }
                }
            }
        }
    }

    /// Fetches the song that plays next into the cache, so it starts at once when its turn comes.
    /// For YouTube songs this hides the server's 5-10 s yt-dlp fetch entirely.
    fn prefetch_next(&self) {
        let Some(next) = self.queue.peek_next() else { return };
        if self.queue.current().is_some_and(|current| current.audio == next.audio) {
            return; // repeat-one: it's already playing
        }
        let base = self.server.read().expect("server url lock").clone();
        self.cache.prefetch(base, next.audio.clone());
    }

    /// Stops whatever plays and starts loading the queue's current item (if any).
    fn start(&mut self, output: &mut Output) {
        self.generation += 1;
        self.loaded = false;
        self.error = None;
        if let Err(error) = output.clear() {
            self.error = Some(error);
            self.buffering = false;
            self.want_playing = false;
            return;
        }
        let Some(item) = self.queue.current().cloned() else {
            self.buffering = false;
            self.duration = None;
            self.want_playing = false;
            return;
        };
        self.duration = item.duration;
        self.buffering = true;
        let generation = self.generation;
        let cache = Arc::clone(&self.cache);
        let base = self.server.read().expect("server url lock").clone();
        let outbox = self.outbox.clone();
        thread::spawn(move || {
            let result = cache
                .open(&base, &item.audio)
                .map_err(|e| e.message)
                .and_then(|audio| Decoder::open(audio.reader, audio.extension.as_deref()).map_err(|e| e.to_string()))
                .map(DecodedSource::new);
            let _ = outbox.send(Command::Loaded { generation, result });
        });
    }

    fn pause(&mut self, output: &mut Output) {
        self.want_playing = false;
        output.player.pause();
    }

    fn resume(&mut self, output: &mut Output) {
        self.want_playing = true;
        if self.loaded {
            output.player.play();
        } else if !self.buffering {
            self.start(output);
        }
    }

    /// The Next button: always another song (repeat-one only replays songs that end).
    fn next(&mut self, output: &mut Output) {
        self.queue.advance(false);
        self.start(output);
    }

    fn seek(&mut self, seconds: f64, output: &mut Output) {
        if self.loaded
            && let Err(error) = output.seek(Duration::from_secs_f64(seconds.max(0.0)))
        {
            log::warn!("seek failed: {error}");
        }
    }

    fn snapshot(&self, output: &Output) -> PlayerState {
        let position = if self.loaded { output.player.get_pos().as_secs_f64() } else { 0.0 };
        PlayerState {
            current_item: self.queue.current().cloned(),
            index: self.queue.index(),
            queue_len: self.queue.items().len(),
            queue_revision: self.queue.revision(),
            shuffle: self.queue.shuffle(),
            repeat: self.queue.repeat(),
            playing: self.want_playing && self.loaded,
            buffering: self.buffering,
            // Rounded so the state only changes (and is pushed) when the display would.
            position: (position * 4.0).round() / 4.0,
            duration: self.duration,
            volume: self.volume,
            error: self.error.clone(),
            context: self.context.clone(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_queue_sink_forwards_to_the_handle() {
        use crate::player::library::QueueSink;

        // A handle without a player thread (and without audio): the test reads its inbox.
        let (commands, inbox) = mpsc::channel();
        let handle = PlayerHandle {
            commands,
            state: Arc::default(),
            queue: Arc::default(),
            sessions: Arc::new(AtomicU64::new(4)),
            session: Arc::new(AtomicU64::new(3)),
        };

        assert_eq!(QueueSink::new_session(&handle), 5);
        assert_eq!(QueueSink::new_session(&handle), 6);
        assert_eq!(QueueSink::session(&handle), 3);
        QueueSink::send(&handle, Command::Append { session: 6, items: Vec::new() });
        assert!(matches!(inbox.try_recv(), Ok(Command::Append { session: 6, .. })));
    }

    #[test]
    fn stale_session_appends_are_ignored() {
        // Play 1, then Play 2: an append for session 1 is stale.
        assert!(accepts(2, 2));
        assert!(!accepts(2, 1));
        // After Stop the engine takes a new number, so no earlier session matches.
        assert!(!accepts(3, 2));
    }

    #[test]
    fn the_queue_view_has_the_items_order_and_revision() {
        let items: Vec<QueueItem> = (0..3)
            .map(|n| QueueItem {
                audio: shared::AudioRef::Track { id: n.to_string() },
                title: format!("Song {n}"),
                artist: "Band".into(),
                duration: None,
                video_id: None,
            })
            .collect();
        let mut queue = Queue::default();
        queue.replace(items.clone(), 1);
        let view = queue_view(&queue);
        assert_eq!(view.items, items);
        assert_eq!(view.order, vec![0, 1, 2]);
        assert_eq!(view.index, Some(1));
        assert_eq!(view.revision, queue.revision());
        assert_ne!(view.revision, 0);
    }

    #[test]
    fn each_new_session_has_its_own_number() {
        let counter = AtomicU64::new(0);
        let a = next_session(&counter);
        let b = next_session(&counter);
        assert_ne!(a, b);
    }
}
