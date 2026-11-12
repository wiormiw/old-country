//! Decodes real files in every format the server can hand us, generated with ffmpeg at test time
//! (skipped when ffmpeg isn't installed). Covers the libopus path for YouTube's WebM/Opus.

use std::fs::File;
use std::path::{Path, PathBuf};
use std::process::Command;

use rodio::Source;

use super::decoder::Decoder;
use super::source::DecodedSource;

const SECONDS: f64 = 3.0;

/// A 440 Hz stereo tone in `extension`'s format, encoded with `codec_args`.
fn fixture(dir: &Path, extension: &str, codec_args: &[&str]) -> Option<PathBuf> {
    let path = dir.join(format!("tone.{extension}"));
    let status = Command::new("ffmpeg")
        .args(["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i"])
        .arg(format!("sine=frequency=440:sample_rate=48000:duration={SECONDS}"))
        .args(["-ac", "2"])
        .args(codec_args)
        .arg(&path)
        .status()
        .ok()?;
    status.success().then_some(path)
}

fn check(extension: &str, codec_args: &[&str]) {
    let dir = tempfile::tempdir().unwrap();
    let Some(path) = fixture(dir.path(), extension, codec_args) else {
        eprintln!("skipping {extension}: ffmpeg is not available");
        return;
    };

    let mut decoder = Decoder::open(Box::new(File::open(&path).unwrap()), Some(extension))
        .unwrap_or_else(|e| panic!("{extension}: {e}"));
    if let Some(duration) = decoder.duration() {
        assert!((duration - SECONDS).abs() < 0.2, "{extension}: duration {duration}");
    }

    // Seek to the middle and keep decoding from there.
    let landed = decoder.seek(1.5).unwrap_or_else(|e| panic!("{extension} seek: {e}"));
    assert!((0.0..=1.6).contains(&landed), "{extension}: seek landed at {landed}");

    let source = DecodedSource::new(decoder);
    assert_eq!(source.channels().get(), 2, "{extension}: channels");
    assert_eq!(source.sample_rate().get(), 48_000, "{extension}: rate");
    let samples = source.count();
    let decoded_seconds = samples as f64 / 2.0 / 48_000.0;
    // From ~1.5 s to the end: about 1.5 s of audio (codec priming adds a little).
    assert!((1.0..=2.2).contains(&decoded_seconds), "{extension}: decoded {decoded_seconds} s after the seek");
}

#[test]
fn flac() {
    check("flac", &["-c:a", "flac"]);
}

#[test]
fn mp3() {
    check("mp3", &["-c:a", "libmp3lame", "-b:a", "192k"]);
}

#[test]
fn aac_in_m4a() {
    check("m4a", &["-c:a", "aac", "-b:a", "160k"]);
}

#[test]
fn vorbis_in_ogg() {
    check("ogg", &["-c:a", "libvorbis"]);
}

#[test]
fn opus_in_ogg_like_downloads() {
    check("opus", &["-c:a", "libopus", "-b:a", "128k"]);
}

#[test]
fn opus_in_webm_like_youtube_streams() {
    check("webm", &["-c:a", "libopus", "-b:a", "128k"]);
}

#[test]
fn wav() {
    check("wav", &["-c:a", "pcm_s16le"]);
}
