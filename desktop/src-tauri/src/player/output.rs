//! The sound card connection, open only while there's something to hear.
//!
//! An open output stream runs all the time: when nothing plays, it plays silence. Desktops see
//! that as an app making sound (KDE's task bar shows a speaker on the app, and the stream holds
//! the device awake). So the stream is opened when playback starts and closed a few seconds
//! after it pauses or stops.
//!
//! rodio's `Player` hands its sound queue to the stream's mixer, and closing the stream would
//! drop the queue with the paused song in it. So the queue is kept here, behind a shared handle:
//! each new stream gets a handle to the same queue, and the song and its position survive.

use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use rodio::queue::SourcesQueueOutput;
use rodio::source::SeekError;
use rodio::{ChannelCount, SampleRate, Source};

/// How long the stream stays open after playback pauses or stops, so quick pause/resume
/// doesn't reopen the device each time.
const CLOSE_AFTER: Duration = Duration::from_secs(3);

/// Output buffer in frames (~85 ms at 48 kHz). rodio's default (~43 ms) underruns when the
/// desktop is busy; music playback doesn't need low latency, it needs no crackles.
const OUTPUT_BUFFER_FRAMES: u32 = 4_096;

pub struct Output {
    /// The controls: play, pause, volume, position. Use [`Output::clear`] and [`Output::seek`]
    /// rather than the player's own, which wait for the sound card and would hang while it's closed.
    pub player: rodio::Player,
    queue: Shared,
    device: Option<rodio::MixerDeviceSink>,
    /// When playback stopped needing the open stream.
    idle_since: Option<Instant>,
}

impl Output {
    /// The player and its output, closed for now.
    pub fn new() -> Self {
        let (player, queue) = rodio::Player::new();
        Self { player, queue: Shared(Arc::new(Mutex::new(queue))), device: None, idle_since: None }
    }

    /// Drops whatever is queued and pauses. rodio does the dropping inside the sound card's
    /// callback and waits for it, so the stream is opened first when there's something to drop.
    pub fn clear(&mut self) -> Result<(), String> {
        if !self.player.empty() {
            self.open()?;
        }
        self.player.clear();
        Ok(())
    }

    /// Seeks the current song. Like [`Output::clear`], rodio waits for the sound card to do it.
    pub fn seek(&mut self, position: Duration) -> Result<(), String> {
        if !self.player.empty() {
            self.open()?;
        }
        self.player.try_seek(position).map_err(|error| error.to_string())
    }

    /// Opens the stream if it's closed. An error says why there's no sound.
    pub fn open(&mut self) -> Result<(), String> {
        self.idle_since = None;
        if self.device.is_some() {
            return Ok(());
        }
        let device = open_device().map_err(|error| {
            log::error!("no audio output: {error}");
            format!("No audio output device is available: {error}")
        })?;
        device.mixer().add(self.queue.clone());
        self.device = Some(device);
        log::debug!("audio output opened");
        Ok(())
    }

    /// Called on every tick: closes the stream once it has been unneeded for a moment.
    pub fn close_when_idle(&mut self, needed: bool, now: Instant) {
        if needed || self.device.is_none() {
            self.idle_since = None;
            return;
        }
        let since = *self.idle_since.get_or_insert(now);
        if now.duration_since(since) >= CLOSE_AFTER {
            self.device = None; // drops the stream; the queue lives on in `self.queue`
            self.idle_since = None;
            log::debug!("audio output closed while idle");
        }
    }

    #[cfg(test)]
    fn is_open(&self) -> bool {
        self.device.is_some()
    }
}

/// The default device with a roomier buffer, falling back to rodio's own choice of device and
/// settings if that's refused.
fn open_device() -> Result<rodio::MixerDeviceSink, rodio::DeviceSinkError> {
    let mut device = rodio::DeviceSinkBuilder::from_default_device()
        .and_then(|builder| builder.with_buffer_size(rodio::cpal::BufferSize::Fixed(OUTPUT_BUFFER_FRAMES)).open_stream())
        .or_else(|error| {
            log::warn!("couldn't open audio with a {OUTPUT_BUFFER_FRAMES}-frame buffer ({error}); using defaults");
            rodio::DeviceSinkBuilder::open_default_sink()
        })?;
    device.log_on_drop(false); // closing it is routine here
    Ok(device)
}

/// A handle to the player's sound queue that a stream's mixer can own while another handle
/// stays here. The lock is only ever taken by the one open stream's callback (and briefly when
/// streams change), so it's never contended.
#[derive(Clone)]
struct Shared(Arc<Mutex<SourcesQueueOutput>>);

impl Shared {
    fn with<T>(&self, f: impl FnOnce(&mut SourcesQueueOutput) -> T) -> T {
        f(&mut self.0.lock().unwrap_or_else(std::sync::PoisonError::into_inner))
    }
}

impl Iterator for Shared {
    type Item = rodio::Sample;

    fn next(&mut self) -> Option<Self::Item> {
        self.with(Iterator::next)
    }
}

impl Source for Shared {
    fn current_span_len(&self) -> Option<usize> {
        self.with(|queue| queue.current_span_len())
    }

    fn channels(&self) -> ChannelCount {
        self.with(|queue| queue.channels())
    }

    fn sample_rate(&self) -> SampleRate {
        self.with(|queue| queue.sample_rate())
    }

    fn total_duration(&self) -> Option<Duration> {
        None // the queue never ends
    }

    fn try_seek(&mut self, position: Duration) -> Result<(), SeekError> {
        self.with(|queue| queue.try_seek(position))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn closed() -> Output {
        Output::new()
    }

    /// Runs `f` on another thread and fails if it doesn't finish within two seconds.
    fn finishes(f: impl FnOnce() + Send + 'static) -> bool {
        let (done, finished) = std::sync::mpsc::channel();
        std::thread::spawn(move || {
            f();
            let _ = done.send(());
        });
        finished.recv_timeout(Duration::from_secs(2)).is_ok()
    }

    fn song() -> impl Source + Send {
        rodio::source::SineWave::new(440.0).take_duration(Duration::from_secs(60))
    }

    #[test]
    fn clearing_or_seeking_a_paused_song_after_the_stream_closed_does_not_hang() {
        // The bug: pause, wait for the stream to close, pick another song -> the player thread hung.
        assert!(finishes(|| {
            let mut output = closed();
            if output.open().is_err() {
                return; // no sound card here (CI): nothing to check
            }
            output.player.append(song());
            output.player.pause();
            output.device = None; // closed while paused
            output.seek(Duration::from_secs(5)).expect("seek");
            output.device = None;
            output.clear().expect("clear");
        }));
    }

    #[test]
    fn clearing_an_empty_player_needs_no_stream() {
        let mut output = closed();
        assert!(output.clear().is_ok());
        assert!(!output.is_open());
    }

    #[test]
    fn a_closed_output_stays_closed_and_idle_untracked() {
        let mut output = closed();
        output.close_when_idle(false, Instant::now());
        assert!(!output.is_open());
        assert!(output.idle_since.is_none());
    }

    #[test]
    fn the_shared_queue_plays_what_the_player_appends() {
        let output = Output::new();
        output.player.append(rodio::source::SineWave::new(440.0).take_duration(Duration::from_millis(10)));
        let mut handle = output.queue.clone();
        // A second handle (as a reopened stream would get) reads the same queue.
        assert!(handle.by_ref().take(64).any(|sample| sample != 0.0));
    }
}
