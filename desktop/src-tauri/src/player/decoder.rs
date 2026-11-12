//! Decoding with symphonia 0.6, plus libopus for Opus (symphonia has no Opus decoder of its own,
//! and YouTube audio is Opus). Turns any supported file into interleaved `f32` samples.
//!
//! rodio's built-in decoder can't be used for this: rodio 0.23 is built on symphonia 0.5, and the
//! libopus adapter needs symphonia 0.6. So rodio only plays the samples produced here.

use std::sync::OnceLock;

use symphonia::core::codecs::audio::{AudioDecoder as SymphoniaDecoder, AudioDecoderOptions};
use symphonia::core::codecs::registry::CodecRegistry;
use symphonia::core::codecs::CodecParameters;
use symphonia::core::errors::Error as SymphoniaError;
use symphonia::core::formats::probe::Hint;
use symphonia::core::formats::{FormatOptions, FormatReader, SeekMode, SeekTo, TrackType};
use symphonia::core::io::{MediaSource, MediaSourceStream, MediaSourceStreamOptions};
use symphonia::core::meta::MetadataOptions;
use symphonia::core::units::{Time, TimeBase};
use symphonia_adapter_libopus::OpusDecoder;

/// Symphonia's built-in decoders plus libopus.
fn codecs() -> &'static CodecRegistry {
    static REGISTRY: OnceLock<CodecRegistry> = OnceLock::new();
    REGISTRY.get_or_init(|| {
        let mut registry = CodecRegistry::new();
        symphonia::default::register_enabled_codecs(&mut registry);
        registry.register_audio_decoder::<OpusDecoder>();
        registry
    })
}

#[derive(Debug, thiserror::Error)]
pub enum DecodeError {
    #[error("this file's format isn't supported ({0})")]
    Unsupported(String),
    #[error("the audio couldn't be read: {0}")]
    Read(String),
}

impl From<SymphoniaError> for DecodeError {
    fn from(error: SymphoniaError) -> Self {
        match error {
            SymphoniaError::Unsupported(what) => DecodeError::Unsupported(what.to_owned()),
            other => DecodeError::Read(other.to_string()),
        }
    }
}

/// One open audio stream, decoded a packet at a time.
pub struct Decoder {
    format: Box<dyn FormatReader + 'static>,
    decoder: Box<dyn SymphoniaDecoder>,
    track_id: u32,
    time_base: Option<TimeBase>,
    channels: u16,
    sample_rate: u32,
    duration: Option<f64>,
}

impl Decoder {
    /// Probes the stream and opens a decoder for its default audio track. `extension` (e.g.
    /// "flac", "webm") speeds up probing; the content is still checked.
    pub fn open(source: Box<dyn MediaSource>, extension: Option<&str>) -> Result<Self, DecodeError> {
        let stream = MediaSourceStream::new(source, MediaSourceStreamOptions::default());
        let mut hint = Hint::new();
        if let Some(extension) = extension {
            hint.with_extension(extension);
        }

        let format = symphonia::default::get_probe().probe(
            &hint,
            stream,
            FormatOptions::default(),
            MetadataOptions::default(),
        )?;

        let track = format
            .default_track(TrackType::Audio)
            .ok_or_else(|| DecodeError::Unsupported("no audio track".into()))?;
        let Some(CodecParameters::Audio(params)) = &track.codec_params else {
            return Err(DecodeError::Unsupported("no audio codec parameters".into()));
        };
        let decoder = codecs().make_audio_decoder(params, &AudioDecoderOptions::default())?;

        let sample_rate = params.sample_rate.unwrap_or(0);
        let channels = params.channels.as_ref().map_or(0, |c| c.count()) as u16;
        let time_base = track.time_base;
        let duration = match (track.num_frames, sample_rate) {
            (Some(frames), rate) if rate > 0 => Some(frames as f64 / f64::from(rate)),
            _ => track
                .duration
                .zip(time_base)
                .and_then(|(duration, base)| base.calc_time(symphonia::core::units::Timestamp::new(duration.get() as i64)))
                .map(|time| time.as_secs_f64()),
        };

        Ok(Self { track_id: track.id, time_base, channels, sample_rate, duration, format, decoder })
    }

    /// Channels and rate as declared by the container; the first decoded packet may refine them.
    pub fn channels(&self) -> u16 {
        self.channels
    }

    pub fn sample_rate(&self) -> u32 {
        self.sample_rate
    }

    /// Length in seconds, when the container says.
    pub fn duration(&self) -> Option<f64> {
        self.duration
    }

    /// Decodes the next packet of this track, appending interleaved samples to `out`.
    /// Returns `false` at the end of the stream. Corrupt packets are skipped, as players do.
    pub fn decode_next(&mut self, out: &mut Vec<f32>) -> Result<bool, DecodeError> {
        loop {
            let packet = match self.format.next_packet() {
                Ok(Some(packet)) => packet,
                Ok(None) => return Ok(false),
                Err(SymphoniaError::IoError(error)) if error.kind() == std::io::ErrorKind::UnexpectedEof => return Ok(false),
                Err(error) => return Err(error.into()),
            };
            if packet.track_id != self.track_id {
                continue;
            }
            match self.decoder.decode(&packet) {
                Ok(buffer) => {
                    let spec = buffer.spec();
                    self.sample_rate = spec.rate();
                    self.channels = spec.channels().count() as u16;
                    buffer.copy_to_vec_interleaved(out);
                    return Ok(true);
                }
                Err(SymphoniaError::DecodeError(reason)) => {
                    log::debug!("skipping a corrupt packet: {reason}");
                    continue;
                }
                Err(error) => return Err(error.into()),
            }
        }
    }

    /// Seeks to `seconds`; returns where decoding actually resumes (the nearest earlier
    /// packet boundary).
    pub fn seek(&mut self, seconds: f64) -> Result<f64, DecodeError> {
        let time = Time::try_from_secs_f64(seconds.max(0.0)).unwrap_or(Time::ZERO);
        let seeked = self
            .format
            .seek(SeekMode::Accurate, SeekTo::Time { time, track_id: Some(self.track_id) })?;
        self.decoder.reset();
        Ok(self
            .time_base
            .and_then(|base| base.calc_time(seeked.actual_ts))
            .map_or(seconds, |time| time.as_secs_f64()))
    }
}
