//! Fetches audio from the server into a local cache while it plays.
//!
//! A track's bytes stream into `<cache>/<key>.<ext>.part`; a [`ProgressiveReader`] reads that same
//! file and, when it gets ahead of the download, waits for more bytes instead of failing. So
//! playback starts after the first chunk, seeking works anywhere (it waits briefly for bytes not
//! yet fetched), and a finished file is renamed into place and replays without the network.
//! The cache is trimmed least-recently-played first to a size limit.

use std::collections::HashMap;
use std::fs::{self, File};
use std::io::{self, Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};
use std::sync::{Arc, Condvar, Mutex};
use std::time::SystemTime;

use futures_util::StreamExt;
use shared::{AppError, AudioRef, ProblemDetails};
use symphonia::core::io::MediaSource;

/// Audio opened for playback: a reader and a format hint for the decoder.
pub struct OpenedAudio {
    pub reader: Box<dyn MediaSource>,
    pub extension: Option<String>,
    pub from_cache: bool,
}

#[derive(Default)]
struct Progress {
    written: u64,
    total: Option<u64>,
    done: bool,
    failed: Option<String>,
}

#[derive(Default)]
struct Shared {
    progress: Mutex<Progress>,
    changed: Condvar,
}

impl Shared {
    fn update(&self, change: impl FnOnce(&mut Progress)) {
        change(&mut self.progress.lock().expect("progress lock"));
        self.changed.notify_all();
    }
}

/// Reads a file that may still be downloading.
pub struct ProgressiveReader {
    file: File,
    position: u64,
    shared: Arc<Shared>,
}

impl Read for ProgressiveReader {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        let available = {
            let mut progress = self.shared.progress.lock().expect("progress lock");
            // Wait until the bytes at our position exist, the download ends, or it fails.
            while progress.written <= self.position && !progress.done && progress.failed.is_none() {
                progress = self.shared.changed.wait(progress).expect("progress lock");
            }
            if let Some(reason) = &progress.failed {
                return Err(io::Error::other(reason.clone()));
            }
            progress.written.saturating_sub(self.position)
        };
        if available == 0 {
            return Ok(0); // end of a finished download
        }
        let want = buf.len().min(usize::try_from(available).unwrap_or(usize::MAX));
        self.file.seek(SeekFrom::Start(self.position))?;
        let read = self.file.read(&mut buf[..want])?;
        self.position += read as u64;
        Ok(read)
    }
}

impl Seek for ProgressiveReader {
    fn seek(&mut self, to: SeekFrom) -> io::Result<u64> {
        let target = match to {
            SeekFrom::Start(offset) => offset as i64,
            SeekFrom::Current(delta) => self.position as i64 + delta,
            SeekFrom::End(delta) => {
                // Wait for the length: the response headers usually give it straight away.
                let mut progress = self.shared.progress.lock().expect("progress lock");
                while progress.total.is_none() && !progress.done && progress.failed.is_none() {
                    progress = self.shared.changed.wait(progress).expect("progress lock");
                }
                let end = progress.total.unwrap_or(progress.written);
                end as i64 + delta
            }
        };
        if target < 0 {
            return Err(io::Error::new(io::ErrorKind::InvalidInput, "seek before the start"));
        }
        self.position = target as u64;
        Ok(self.position)
    }
}

impl MediaSource for ProgressiveReader {
    fn is_seekable(&self) -> bool {
        true
    }

    /// Unknown until the download finishes. Reporting it earlier would make symphonia's probe
    /// look for trailing metadata at the end of the file and wait for the whole download.
    fn byte_len(&self) -> Option<u64> {
        let progress = self.shared.progress.lock().expect("progress lock");
        progress.done.then_some(progress.written)
    }
}

/// A download in progress, which later readers join instead of downloading again.
struct InFlight {
    part: PathBuf,
    extension: Option<String>,
    shared: Arc<Shared>,
}

type InFlightMap = Arc<Mutex<HashMap<String, InFlight>>>;

/// The local audio cache.
pub struct AudioCache {
    dir: PathBuf,
    http: reqwest::Client,
    runtime: tokio::runtime::Handle,
    in_flight: InFlightMap,
    /// One "start a download" per key at a time, so a prefetch and a play of the same song
    /// can't both start one. Per key, so a slow YouTube fetch never holds up other songs.
    starting: Mutex<HashMap<String, Arc<Mutex<()>>>>,
}

impl AudioCache {
    pub fn new(dir: PathBuf, http: reqwest::Client, runtime: tokio::runtime::Handle) -> io::Result<Self> {
        fs::create_dir_all(&dir)?;
        Ok(Self { dir, http, runtime, in_flight: Arc::default(), starting: Mutex::default() })
    }

    /// Opens audio for playback: from the cache when it's complete there, by joining a download
    /// already running (a prefetch), or by starting one from `base_url` and returning as soon as
    /// the response headers arrive. Blocking: call it from a loader thread, not the UI.
    pub fn open(&self, base_url: &str, audio: &AudioRef) -> Result<OpenedAudio, AppError> {
        let key = audio.cache_key();
        let gate = Arc::clone(self.starting.lock().expect("starting lock").entry(key.clone()).or_default());
        let _starting = gate.lock().expect("start gate");

        if let Some(opened) = self.open_existing(&key)? {
            return Ok(opened);
        }

        let url = format!("{}{}", base_url.trim_end_matches('/'), audio.path());
        let response = self.runtime.block_on(self.http.get(&url).send()).map_err(unreachable)?;
        if !response.status().is_success() {
            let status = response.status().as_u16();
            let problem = self.runtime.block_on(response.json::<ProblemDetails>()).unwrap_or_default();
            return Err(AppError::from_problem(status, problem));
        }

        let extension = extension_for(response.headers().get(reqwest::header::CONTENT_TYPE).and_then(|v| v.to_str().ok()));
        let final_path = self.dir.join(format!("{key}.{}", extension.as_deref().unwrap_or("bin")));
        let part_path = final_path.with_extension(format!("{}.part", extension.as_deref().unwrap_or("bin")));
        let writer = File::create(&part_path).map_err(|e| AppError::new("cache.unwritable", e.to_string()))?;
        let reader_file = File::open(&part_path).map_err(|e| AppError::new("cache.unreadable", e.to_string()))?;

        let shared = Arc::new(Shared::default());
        shared.update(|p| p.total = response.content_length());
        self.in_flight.lock().expect("in-flight lock").insert(
            key.clone(),
            InFlight { part: part_path.clone(), extension: extension.clone(), shared: Arc::clone(&shared) },
        );
        let job = Download { key, part: part_path, done: final_path, shared: Arc::clone(&shared), in_flight: Arc::clone(&self.in_flight) };
        self.runtime.spawn(job.run(response, writer));

        Ok(OpenedAudio {
            reader: Box::new(ProgressiveReader { file: reader_file, position: 0, shared }),
            extension,
            from_cache: false,
        })
    }

    /// Starts fetching `audio` into the cache in the background, so it plays at once later.
    /// Does nothing when it's cached or already downloading.
    pub fn prefetch(self: &Arc<Self>, base_url: String, audio: AudioRef) {
        let cache = Arc::clone(self);
        std::thread::spawn(move || match cache.open(&base_url, &audio) {
            Ok(opened) if !opened.from_cache => log::debug!("prefetching {}", audio.cache_key()),
            Ok(_) => {}
            Err(error) => log::debug!("prefetch of {} failed: {}", audio.cache_key(), error.message),
        });
    }

    /// A finished cached file, or a reader joining a download in progress.
    fn open_existing(&self, key: &str) -> Result<Option<OpenedAudio>, AppError> {
        // Under the in-flight lock, a download can't finish (rename its file) mid-way through.
        let in_flight = self.in_flight.lock().expect("in-flight lock");
        if let Some(download) = in_flight.get(key) {
            let file = File::open(&download.part).map_err(|e| AppError::new("cache.unreadable", e.to_string()))?;
            return Ok(Some(OpenedAudio {
                reader: Box::new(ProgressiveReader { file, position: 0, shared: Arc::clone(&download.shared) }),
                extension: download.extension.clone(),
                from_cache: false,
            }));
        }
        let Some(path) = self.find_cached(key) else { return Ok(None) };
        touch(&path);
        let file = File::open(&path).map_err(|e| AppError::new("cache.unreadable", e.to_string()))?;
        Ok(Some(OpenedAudio {
            reader: Box::new(file),
            extension: path.extension().and_then(|e| e.to_str()).map(str::to_owned),
            from_cache: true,
        }))
    }

    fn find_cached(&self, key: &str) -> Option<PathBuf> {
        let prefix = format!("{key}.");
        fs::read_dir(&self.dir).ok()?.filter_map(Result::ok).map(|entry| entry.path()).find(|path| {
            path.file_name().and_then(|n| n.to_str()).is_some_and(|name| name.starts_with(&prefix) && !name.ends_with(".part"))
        })
    }

    /// Deletes least recently played files until the cache fits in `max_bytes`.
    pub fn trim(&self, max_bytes: u64) {
        trim_dir(&self.dir, max_bytes);
    }
}

struct Download {
    key: String,
    part: PathBuf,
    done: PathBuf,
    shared: Arc<Shared>,
    in_flight: InFlightMap,
}

impl Download {
    async fn run(self, response: reqwest::Response, mut writer: File) {
        let mut stream = response.bytes_stream();
        while let Some(chunk) = stream.next().await {
            let result = chunk
                .map_err(|e| e.to_string())
                .and_then(|bytes| writer.write_all(&bytes).map(|()| bytes.len()).map_err(|e| e.to_string()));
            match result {
                Ok(len) => self.shared.update(|p| p.written += len as u64),
                Err(reason) => {
                    log::warn!("audio download failed: {reason}");
                    let mut in_flight = self.in_flight.lock().expect("in-flight lock");
                    let _ = fs::remove_file(&self.part);
                    in_flight.remove(&self.key);
                    self.shared.update(|p| p.failed = Some(reason));
                    return;
                }
            }
        }
        let _ = writer.flush();
        // Under the in-flight lock, so no reader is joining while the file moves. Readers already
        // reading keep their open handle across the rename (same inode).
        let mut in_flight = self.in_flight.lock().expect("in-flight lock");
        if let Err(error) = fs::rename(&self.part, &self.done) {
            log::warn!("couldn't move a finished download into the cache: {error}");
        }
        in_flight.remove(&self.key);
        self.shared.update(|p| p.done = true);
    }
}

fn unreachable(error: reqwest::Error) -> AppError {
    AppError::new("server.unreachable", format!("The server couldn't be reached: {error}"))
}

/// File extension for the server's audio content types.
fn extension_for(content_type: Option<&str>) -> Option<String> {
    let mime = content_type?.split(';').next()?.trim();
    Some(
        match mime {
            "audio/flac" | "audio/x-flac" => "flac",
            "audio/mpeg" => "mp3",
            "audio/mp4" | "audio/x-m4a" => "m4a",
            "audio/aac" => "aac",
            "audio/ogg" | "audio/opus" => "ogg",
            "audio/webm" => "webm",
            "audio/wav" | "audio/x-wav" | "audio/wave" => "wav",
            _ => return None,
        }
        .to_owned(),
    )
}

/// Marks a file as just played (its modification time drives eviction order).
fn touch(path: &Path) {
    if let Ok(file) = File::options().append(true).open(path) {
        let _ = file.set_modified(SystemTime::now());
    }
}

fn trim_dir(dir: &Path, max_bytes: u64) {
    let Ok(entries) = fs::read_dir(dir) else { return };
    let mut files: Vec<(PathBuf, u64, SystemTime)> = entries
        .filter_map(Result::ok)
        .filter(|e| !e.file_name().to_string_lossy().ends_with(".part"))
        .filter_map(|e| {
            let meta = e.metadata().ok()?;
            meta.is_file().then(|| (e.path(), meta.len(), meta.modified().unwrap_or(SystemTime::UNIX_EPOCH)))
        })
        .collect();
    files.sort_by_key(|file| std::cmp::Reverse(file.2)); // newest first
    let mut total = 0u64;
    for (path, len, _) in files {
        total += len;
        if total > max_bytes {
            let _ = fs::remove_file(path);
        }
    }
}

#[cfg(test)]
mod tests {
    use std::thread;
    use std::time::Duration;

    use super::*;

    fn reader_over(path: &Path, shared: &Arc<Shared>) -> ProgressiveReader {
        ProgressiveReader { file: File::open(path).unwrap(), position: 0, shared: Arc::clone(shared) }
    }

    #[test]
    fn reads_wait_for_bytes_that_have_not_arrived_yet() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("a.part");
        let mut writer = File::create(&path).unwrap();
        let shared = Arc::new(Shared::default());
        let mut reader = reader_over(&path, &shared);

        let feeder_shared = Arc::clone(&shared);
        let feeder = thread::spawn(move || {
            for chunk in [b"hello ".as_slice(), b"world".as_slice()] {
                thread::sleep(Duration::from_millis(30));
                writer.write_all(chunk).unwrap();
                writer.flush().unwrap();
                feeder_shared.update(|p| p.written += chunk.len() as u64);
            }
            feeder_shared.update(|p| p.done = true);
        });

        let mut all = String::new();
        reader.read_to_string(&mut all).unwrap();
        feeder.join().unwrap();
        assert_eq!(all, "hello world");
    }

    #[test]
    fn seeking_ahead_then_reading_waits_for_those_bytes() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("b.part");
        let mut writer = File::create(&path).unwrap();
        let shared = Arc::new(Shared::default());
        shared.update(|p| p.total = Some(10));
        let mut reader = reader_over(&path, &shared);

        assert_eq!(reader.seek(SeekFrom::End(-2)).unwrap(), 8);
        let feeder_shared = Arc::clone(&shared);
        let feeder = thread::spawn(move || {
            thread::sleep(Duration::from_millis(30));
            writer.write_all(b"0123456789").unwrap();
            writer.flush().unwrap();
            feeder_shared.update(|p| {
                p.written = 10;
                p.done = true;
            });
        });

        let mut tail = [0u8; 2];
        reader.read_exact(&mut tail).unwrap();
        feeder.join().unwrap();
        assert_eq!(&tail, b"89");
    }

    #[test]
    fn a_failed_download_surfaces_as_a_read_error() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("c.part");
        File::create(&path).unwrap();
        let shared = Arc::new(Shared::default());
        shared.update(|p| p.failed = Some("connection reset".into()));

        let error = reader_over(&path, &shared).read(&mut [0u8; 4]).unwrap_err();
        assert_eq!(error.to_string(), "connection reset");
    }

    #[test]
    fn the_length_is_hidden_until_the_download_finishes() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("d.part");
        File::create(&path).unwrap();
        let shared = Arc::new(Shared::default());
        shared.update(|p| {
            p.total = Some(100);
            p.written = 40;
        });
        let reader = reader_over(&path, &shared);
        assert_eq!(reader.byte_len(), None);
        shared.update(|p| {
            p.written = 100;
            p.done = true;
        });
        assert_eq!(reader.byte_len(), Some(100));
    }

    #[test]
    fn trimming_keeps_the_most_recently_played_files() {
        let dir = tempfile::tempdir().unwrap();
        for (name, age_secs) in [("old.flac", 300), ("mid.flac", 200), ("new.flac", 100)] {
            let path = dir.path().join(name);
            fs::write(&path, vec![0u8; 1_000]).unwrap();
            File::options()
                .append(true)
                .open(&path)
                .unwrap()
                .set_modified(SystemTime::now() - Duration::from_secs(age_secs))
                .unwrap();
        }
        fs::write(dir.path().join("live.flac.part"), vec![0u8; 5_000]).unwrap();

        trim_dir(dir.path(), 2_000);

        let mut left: Vec<String> =
            fs::read_dir(dir.path()).unwrap().map(|e| e.unwrap().file_name().to_string_lossy().into_owned()).collect();
        left.sort();
        assert_eq!(left, ["live.flac.part", "mid.flac", "new.flac"]);
    }

    #[test]
    fn content_types_map_to_extensions_the_decoder_knows() {
        assert_eq!(extension_for(Some("audio/webm")).as_deref(), Some("webm"));
        assert_eq!(extension_for(Some("audio/flac; charset=binary")).as_deref(), Some("flac"));
        assert_eq!(extension_for(Some("application/json")), None);
        assert_eq!(extension_for(None), None);
    }
}
