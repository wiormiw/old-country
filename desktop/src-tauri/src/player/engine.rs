//! The player thread. It owns the audio output (rodio's device sink isn't `Send`, so it lives on
//! one thread), the queue and the playback state; everything else talks to it through
//! [`Command`]s and hears back through the `emit` callback.
//!
//! Loading a track (fetch + probe) runs on a short-lived loader thread so the engine stays
//! responsive; a generation number discards loads that finish after the user moved on.

use std::sync::mpsc::{self, RecvTimeoutError, Sender};
use std::sync::{Arc, Mutex, RwLock};
use std::thread;
use std::time::Duration;

use shared::{PlayerState, QueueItem, RepeatMode};

use super::cache::AudioCache;
use super::decoder::Decoder;
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
    Play { items: Vec<QueueItem>, start: usize },
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
        let engine = Engine {
            inbox,
            outbox: commands.clone(),
            cache,
            server,
            published: Arc::clone(&state),
            queue,
            generation: 0,
            loaded: false,
            want_playing: false,
            buffering: false,
            duration: None,
            volume,
            error: None,
            failures: 0,
        };
        thread::Builder::new()
            .name("player".into())
            .spawn(move || engine.run(emit))
            .expect("spawn the player thread");
        Self { commands, state }
    }

    pub fn send(&self, command: Command) {
        // Only fails if the player thread is gone, which ends the app anyway.
        let _ = self.commands.send(command);
    }

    pub fn state(&self) -> PlayerState {
        self.state.lock().expect("player state lock").clone()
    }
}

/// Output buffer in frames (~85 ms at 48 kHz). rodio's default (~43 ms) underruns when the
/// desktop is busy; music playback doesn't need low latency, it needs no crackles.
const OUTPUT_BUFFER_FRAMES: u32 = 4_096;

/// The default device with a roomier buffer, falling back to rodio's own choice of device and
/// settings if that's refused.
fn open_output() -> Result<rodio::MixerDeviceSink, rodio::DeviceSinkError> {
    rodio::DeviceSinkBuilder::from_default_device()
        .and_then(|builder| builder.with_buffer_size(rodio::cpal::BufferSize::Fixed(OUTPUT_BUFFER_FRAMES)).open_stream())
        .or_else(|error| {
            log::warn!("couldn't open audio with a {OUTPUT_BUFFER_FRAMES}-frame buffer ({error}); using defaults");
            rodio::DeviceSinkBuilder::open_default_sink()
        })
}

struct Engine {
    inbox: mpsc::Receiver<Command>,
    outbox: Sender<Command>,
    cache: Arc<AudioCache>,
    server: ServerUrl,
    published: Arc<Mutex<PlayerState>>,
    queue: Queue,
    generation: u64,
    loaded: bool,
    want_playing: bool,
    buffering: bool,
    duration: Option<f64>,
    volume: f32,
    error: Option<String>,
    /// Songs that failed to load in a row.
    failures: u32,
}

impl Engine {
    fn run(mut self, emit: impl Fn(&PlayerState)) {
        // The device sink must outlive the player; if there's no audio device, keep the queue
        // working and say why nothing plays.
        let sink = open_output();
        let output = match &sink {
            Ok(sink) => {
                let player = rodio::Player::connect_new(sink.mixer());
                player.set_volume(self.volume as rodio::Float);
                Some(player)
            }
            Err(error) => {
                log::error!("no audio output: {error}");
                self.error = Some(format!("No audio output device is available: {error}"));
                None
            }
        };

        let mut last = PlayerState::default();
        loop {
            match self.inbox.recv_timeout(TICK) {
                Ok(command) => self.handle(command, output.as_ref()),
                Err(RecvTimeoutError::Timeout) => {}
                Err(RecvTimeoutError::Disconnected) => break,
            }
            if let Some(player) = &output {
                // A loaded track that has nothing left to play has ended.
                if self.loaded && self.want_playing && player.empty() {
                    self.queue.advance(true);
                    self.start(output.as_ref());
                }
            }
            let state = self.snapshot(output.as_ref());
            if state != last {
                *self.published.lock().expect("player state lock") = state.clone();
                emit(&state);
                last = state;
            }
        }
    }

    fn handle(&mut self, command: Command, output: Option<&rodio::Player>) {
        match command {
            Command::Play { items, start } => {
                self.queue.replace(items, start);
                self.want_playing = true;
                self.start(output);
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
                let position = output.map_or(0.0, |p| p.get_pos().as_secs_f64());
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
                if let Some(player) = output {
                    player.set_volume(self.volume as rodio::Float);
                }
            }
            Command::Stop => {
                self.queue.clear();
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
                        if let Some(player) = output {
                            player.append(source);
                            if self.want_playing {
                                player.play();
                            }
                            self.loaded = true;
                        }
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
    fn start(&mut self, output: Option<&rodio::Player>) {
        self.generation += 1;
        self.loaded = false;
        self.error = None;
        if let Some(player) = output {
            player.clear();
        }
        let Some(item) = self.queue.current().cloned() else {
            self.buffering = false;
            self.duration = None;
            self.want_playing = false;
            return;
        };
        self.duration = item.duration;
        if output.is_none() {
            self.error = Some("No audio output device is available.".into());
            return;
        }

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

    fn pause(&mut self, output: Option<&rodio::Player>) {
        self.want_playing = false;
        if let Some(player) = output {
            player.pause();
        }
    }

    fn resume(&mut self, output: Option<&rodio::Player>) {
        self.want_playing = true;
        match output {
            Some(player) if self.loaded => player.play(),
            _ if !self.buffering => self.start(output),
            _ => {}
        }
    }

    /// The Next button: always another song (repeat-one only replays songs that end).
    fn next(&mut self, output: Option<&rodio::Player>) {
        self.queue.advance(false);
        self.start(output);
    }

    fn seek(&mut self, seconds: f64, output: Option<&rodio::Player>) {
        if let (Some(player), true) = (output, self.loaded)
            && let Err(error) = player.try_seek(Duration::from_secs_f64(seconds.max(0.0)))
        {
            log::warn!("seek failed: {error}");
        }
    }

    fn snapshot(&self, output: Option<&rodio::Player>) -> PlayerState {
        let position = match output {
            Some(player) if self.loaded => player.get_pos().as_secs_f64(),
            _ => 0.0,
        };
        PlayerState {
            queue: self.queue.items().to_vec(),
            index: self.queue.index(),
            order: self.queue.order().to_vec(),
            shuffle: self.queue.shuffle(),
            repeat: self.queue.repeat(),
            playing: self.want_playing && self.loaded,
            buffering: self.buffering,
            // Rounded so the state only changes (and is pushed) when the display would.
            position: (position * 4.0).round() / 4.0,
            duration: self.duration,
            volume: self.volume,
            error: self.error.clone(),
        }
    }
}
