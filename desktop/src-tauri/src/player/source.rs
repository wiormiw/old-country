//! Adapts [`Decoder`] to `rodio::Source`, decoding *ahead* on its own thread.
//!
//! rodio pulls samples from inside the sound card's real-time callback. If that callback had to
//! decode packets, or wait for bytes still downloading, any hiccup on a busy system (an app
//! starting, the compositor re-rendering after a display-scaling change) made it miss its
//! deadline, and the output crackled. So a decoder thread fills a queue a few seconds ahead,
//! and the callback only copies samples that are already decoded. If it ever catches up with
//! the decoder (a stalled network), it plays a moment of silence rather than blocking.

use std::num::NonZero;
use std::sync::mpsc::{self, Receiver, Sender, SyncSender, TryRecvError};
use std::thread;
use std::time::Duration;

use rodio::source::SeekError;
use rodio::{ChannelCount, SampleRate, Source};

use super::decoder::Decoder;

/// How many decoded packets wait in the queue: a few seconds for any codec
/// (Opus packets are 20 ms, FLAC ones ~90 ms).
const AHEAD_PACKETS: usize = 256;

/// One decoded packet. An empty `samples` marks the end of the stream for that generation.
struct Chunk {
    generation: u64,
    channels: u16,
    rate: u32,
    samples: Vec<f32>,
}

enum Control {
    Seek { seconds: f64, generation: u64 },
}

pub struct DecodedSource {
    chunks: Receiver<Chunk>,
    control: Sender<Control>,
    current: Vec<f32>,
    position: usize,
    channels: u16,
    rate: u32,
    /// Bumped on every seek; chunks decoded before it are discarded.
    generation: u64,
    ended: bool,
    duration: Option<f64>,
    /// Playback (true): play silence when the decoder falls behind. Tests (false): wait for it.
    fill_gaps: bool,
}

impl DecodedSource {
    /// For playback: never blocks the audio callback.
    pub fn new(decoder: Decoder) -> Self {
        Self::start(decoder, true)
    }

    /// Waits for the decoder instead of filling gaps with silence, so every sample is real.
    #[cfg(test)]
    pub fn blocking(decoder: Decoder) -> Self {
        Self::start(decoder, false)
    }

    fn start(decoder: Decoder, fill_gaps: bool) -> Self {
        let duration = decoder.duration();
        let (channels, rate) = (decoder.channels(), decoder.sample_rate());
        let (chunk_tx, chunks) = mpsc::sync_channel(AHEAD_PACKETS);
        let (control, control_rx) = mpsc::channel();
        thread::Builder::new()
            .name("decoder".into())
            .spawn(move || decode_ahead(decoder, &chunk_tx, &control_rx))
            .expect("spawn a decoder thread");

        let mut source = Self {
            chunks,
            control,
            current: Vec::new(),
            position: 0,
            channels,
            rate,
            generation: 0,
            ended: false,
            duration,
            fill_gaps,
        };
        // Wait (on the loader thread, not the audio one) for the first packet: it fixes the real
        // channel count and rate, and shows an unreadable file before playback starts.
        source.wait_for_first_chunk();
        source
    }

    fn wait_for_first_chunk(&mut self) {
        match self.chunks.recv_timeout(Duration::from_secs(30)) {
            Ok(chunk) if chunk.samples.is_empty() => self.ended = true,
            Ok(chunk) => self.load(chunk),
            Err(_) => self.ended = true,
        }
    }

    fn load(&mut self, chunk: Chunk) {
        self.channels = chunk.channels;
        self.rate = chunk.rate;
        self.current = chunk.samples;
        self.position = 0;
    }

    pub fn duration(&self) -> Option<f64> {
        self.duration
    }
}

/// The decoder thread: decodes into the bounded queue (which blocks when it's full, a few
/// seconds ahead), and handles seeks. Ends when the source is dropped.
fn decode_ahead(mut decoder: Decoder, chunks: &SyncSender<Chunk>, control: &Receiver<Control>) {
    let mut generation = 0;
    let mut buffer = Vec::new();
    loop {
        // Apply the latest seek, if any arrived.
        let mut seek = None;
        loop {
            match control.try_recv() {
                Ok(Control::Seek { seconds, generation }) => seek = Some((seconds, generation)),
                Err(TryRecvError::Empty) => break,
                Err(TryRecvError::Disconnected) => return,
            }
        }
        if let Some((seconds, next)) = seek {
            generation = next;
            if let Err(error) = decoder.seek(seconds) {
                log::warn!("seek failed: {error}");
            }
        }

        buffer.clear();
        let more = decoder.decode_next(&mut buffer).unwrap_or_else(|error| {
            log::warn!("playback stopped: {error}");
            false
        });
        if !more {
            // End of stream: tell the source, then wait for a seek (which restarts decoding) or
            // for the source to go away.
            let end = Chunk { generation, channels: decoder.channels(), rate: decoder.sample_rate(), samples: Vec::new() };
            if chunks.send(end).is_err() {
                return;
            }
            match control.recv() {
                Ok(Control::Seek { seconds, generation: next }) => {
                    generation = next;
                    if let Err(error) = decoder.seek(seconds) {
                        log::warn!("seek failed: {error}");
                    }
                    continue;
                }
                Err(_) => return,
            }
        }
        if buffer.is_empty() {
            // Some packets decode to nothing (Vorbis primes itself after a seek). An empty chunk
            // means "end of stream" to the source, so don't send one.
            continue;
        }
        let chunk = Chunk {
            generation,
            channels: decoder.channels(),
            rate: decoder.sample_rate(),
            samples: std::mem::take(&mut buffer),
        };
        if chunks.send(chunk).is_err() {
            return;
        }
    }
}

impl Iterator for DecodedSource {
    type Item = rodio::Sample;

    fn next(&mut self) -> Option<Self::Item> {
        loop {
            if self.position < self.current.len() {
                let sample = self.current[self.position];
                self.position += 1;
                return Some(sample as rodio::Sample);
            }
            if self.ended {
                return None;
            }
            let next = if self.fill_gaps {
                match self.chunks.try_recv() {
                    Ok(chunk) => Some(chunk),
                    // The decoder is behind: a moment of silence keeps the device fed.
                    Err(TryRecvError::Empty) => return Some(0.0),
                    Err(TryRecvError::Disconnected) => None,
                }
            } else {
                self.chunks.recv_timeout(Duration::from_secs(30)).ok()
            };
            match next {
                Some(chunk) if chunk.generation != self.generation => {} // from before a seek
                Some(chunk) if chunk.samples.is_empty() => self.ended = true,
                Some(chunk) => self.load(chunk),
                None => self.ended = true,
            }
        }
    }
}

impl Source for DecodedSource {
    // Each decoded packet is a span: channels and rate are constant within it. While waiting
    // for the decoder, the next span is at least one frame of silence.
    fn current_span_len(&self) -> Option<usize> {
        let remaining = self.current.len().saturating_sub(self.position);
        if remaining > 0 { Some(remaining) } else { Some(usize::from(self.channels.max(1))) }
    }

    fn channels(&self) -> ChannelCount {
        NonZero::new(self.channels).unwrap_or(NonZero::new(2).expect("2 is non-zero"))
    }

    fn sample_rate(&self) -> SampleRate {
        NonZero::new(self.rate).unwrap_or(NonZero::new(44_100).expect("44100 is non-zero"))
    }

    fn total_duration(&self) -> Option<Duration> {
        self.duration.map(Duration::from_secs_f64)
    }

    /// Doesn't wait: the decoder thread seeks while the callback plays silence briefly.
    fn try_seek(&mut self, position: Duration) -> Result<(), SeekError> {
        self.generation += 1;
        self.control
            .send(Control::Seek { seconds: position.as_secs_f64(), generation: self.generation })
            .map_err(|_| SeekError::Other(std::sync::Arc::new(std::io::Error::other("the decoder has stopped"))))?;
        self.current.clear();
        self.position = 0;
        self.ended = false;
        Ok(())
    }
}
