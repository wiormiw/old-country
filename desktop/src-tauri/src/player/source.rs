//! Adapts [`Decoder`] to `rodio::Source`, so rodio only has to mix and output samples.

use std::num::NonZero;
use std::sync::Arc;
use std::time::Duration;

use rodio::source::SeekError;
use rodio::{ChannelCount, SampleRate, Source};

use super::decoder::Decoder;

pub struct DecodedSource {
    decoder: Decoder,
    buffer: Vec<f32>,
    position: usize,
    finished: bool,
}

impl DecodedSource {
    pub fn new(mut decoder: Decoder) -> Self {
        // Decode the first packet now: it fixes the real channel count and rate (some containers
        // don't declare them), and surfaces an unreadable file before playback starts.
        let mut buffer = Vec::new();
        let finished = !decoder.decode_next(&mut buffer).unwrap_or(false);
        Self { decoder, buffer, position: 0, finished }
    }

    pub fn duration(&self) -> Option<f64> {
        self.decoder.duration()
    }

    fn refill(&mut self) -> bool {
        self.buffer.clear();
        self.position = 0;
        while self.buffer.is_empty() {
            match self.decoder.decode_next(&mut self.buffer) {
                Ok(true) => {}
                Ok(false) => return false,
                Err(error) => {
                    log::warn!("playback stopped: {error}");
                    return false;
                }
            }
        }
        true
    }
}

impl Iterator for DecodedSource {
    type Item = rodio::Sample;

    fn next(&mut self) -> Option<Self::Item> {
        if self.position >= self.buffer.len() && (self.finished || !self.refill()) {
            self.finished = true;
            return None;
        }
        let sample = self.buffer[self.position];
        self.position += 1;
        Some(sample as rodio::Sample)
    }
}

impl Source for DecodedSource {
    // Each decoded packet is a span: channels and rate are constant within it.
    fn current_span_len(&self) -> Option<usize> {
        (!self.finished).then(|| self.buffer.len().saturating_sub(self.position)).filter(|len| *len > 0)
    }

    fn channels(&self) -> ChannelCount {
        NonZero::new(self.decoder.channels()).unwrap_or(NonZero::<u16>::MIN.saturating_add(1))
    }

    fn sample_rate(&self) -> SampleRate {
        NonZero::new(self.decoder.sample_rate()).unwrap_or(NonZero::new(44_100).expect("44100 is non-zero"))
    }

    fn total_duration(&self) -> Option<Duration> {
        self.decoder.duration().map(Duration::from_secs_f64)
    }

    fn try_seek(&mut self, position: Duration) -> Result<(), SeekError> {
        self.decoder
            .seek(position.as_secs_f64())
            .map_err(|error| SeekError::Other(Arc::new(error)))?;
        self.buffer.clear();
        self.position = 0;
        self.finished = false;
        Ok(())
    }
}
