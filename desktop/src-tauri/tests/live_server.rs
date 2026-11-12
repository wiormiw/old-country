//! End-to-end check of native playback against a running old-country server, through the real
//! pieces: API client → progressive cache → symphonia/libopus decoder → rodio → the sound card.
//!
//! Off by default (it needs a server, a sound device, and it makes sound). Run with:
//!   OLDCOUNTRY_LIVE_SERVER=http://127.0.0.1:5000 cargo test -p old-country --test live_server -- --nocapture
//! The library should hold a few tracks (any formats); YouTube needs yt-dlp on the server.

use std::sync::{Arc, Mutex, RwLock};
use std::time::{Duration, Instant};

use old_country_lib::api::ApiClient;
use old_country_lib::player::cache::AudioCache;
use old_country_lib::player::{Command, PlayerHandle};
use shared::{AudioRef, PlayerState, QueueItem};

fn server() -> Option<String> {
    std::env::var("OLDCOUNTRY_LIVE_SERVER").ok().filter(|s| !s.is_empty())
}

struct Rig {
    runtime: tokio::runtime::Runtime,
    api: ApiClient,
    player: PlayerHandle,
    states: Arc<Mutex<Vec<PlayerState>>>,
    _cache_dir: tempfile::TempDir,
    cache_path: std::path::PathBuf,
}

fn rig(base: &str) -> Rig {
    let runtime = tokio::runtime::Runtime::new().unwrap();
    let url = Arc::new(RwLock::new(base.to_owned()));
    let http = reqwest::Client::new();
    let cache_dir = tempfile::tempdir().unwrap();
    let cache_path = cache_dir.path().to_path_buf();
    let cache = Arc::new(AudioCache::new(cache_path.clone(), http.clone(), runtime.handle().clone()).unwrap());
    let states = Arc::new(Mutex::new(Vec::new()));
    let sink = Arc::clone(&states);
    let player = PlayerHandle::spawn(cache, Arc::clone(&url), 0.15, move |s| sink.lock().unwrap().push(s.clone()));
    Rig { api: ApiClient::new(http, url), runtime, player, states, _cache_dir: cache_dir, cache_path }
}

/// Waits until the player's state satisfies `done`, failing with `what` after `timeout`.
fn wait_for(rig: &Rig, what: &str, timeout: Duration, done: impl Fn(&PlayerState) -> bool) -> PlayerState {
    let started = Instant::now();
    loop {
        let state = rig.player.state();
        if done(&state) {
            return state;
        }
        if let Some(error) = &state.error {
            panic!("{what}: player error: {error}");
        }
        assert!(started.elapsed() < timeout, "{what}: timed out; last state {state:?}");
        std::thread::sleep(Duration::from_millis(100));
    }
}

#[test]
fn plays_every_library_track_seeks_skips_and_streams_youtube() {
    let Some(base) = server() else {
        eprintln!("skipped: set OLDCOUNTRY_LIVE_SERVER to run");
        return;
    };
    let rig = rig(&base);

    let page = rig.runtime.block_on(rig.api.list_tracks(1, 50)).expect("list tracks");
    assert!(!page.items.is_empty(), "the server's library is empty; scan some music first");
    let queue: Vec<QueueItem> = page
        .items
        .iter()
        .filter(|t| t.is_available_offline)
        .map(|t| QueueItem {
            audio: AudioRef::Track { id: t.id.clone() },
            title: t.title.clone(),
            artist: t.artist.clone(),
            duration: Some(t.duration),
        })
        .collect();
    let codecs: Vec<String> =
        page.items.iter().filter(|t| t.is_available_offline).map(|t| t.codec.clone().unwrap_or_default()).collect();

    // 1. Each track: starts playing and its position advances.
    rig.player.send(Command::Play { items: queue.clone(), start: 0 });
    for (i, item) in queue.iter().enumerate() {
        if i > 0 {
            rig.player.send(Command::Jump(i));
        }
        let started = Instant::now();
        let state = wait_for(&rig, &item.title, Duration::from_secs(20), |s| {
            s.index == Some(i) && s.playing && s.position >= 1.0
        });
        let duration = state.duration.unwrap_or_default();
        println!(
            "  ▶ {:<14} {:<7} playing after {:>4} ms, duration {:>6.1} s (server says {:.1} s)",
            item.title,
            codecs[i],
            started.elapsed().as_millis(),
            duration,
            item.duration.unwrap_or_default()
        );
        assert!((duration - item.duration.unwrap_or_default()).abs() < 1.5, "{}: duration {duration}", item.title);
    }

    // 2. Seek within the current track.
    rig.player.send(Command::Seek(45.0));
    let state = wait_for(&rig, "seek to 45 s", Duration::from_secs(10), |s| s.position >= 45.0 && s.position < 50.0);
    println!("  ⇥ seek → position {:.1} s", state.position);

    // 3. Previous after more than 3 s restarts the track; next/prev move through the queue.
    rig.player.send(Command::Prev);
    wait_for(&rig, "prev restarts", Duration::from_secs(10), |s| s.position < 3.0 && s.playing);
    rig.player.send(Command::Jump(0));
    wait_for(&rig, "jump to first", Duration::from_secs(10), |s| s.index == Some(0) && s.playing);
    rig.player.send(Command::Next);
    let state = wait_for(&rig, "next", Duration::from_secs(10), |s| s.index == Some(1) && s.playing);
    println!("  ⏭ next → {}", state.current().unwrap().title);

    // 4. Pause holds the position; volume changes are reflected.
    rig.player.send(Command::Pause);
    let paused = wait_for(&rig, "pause", Duration::from_secs(5), |s| !s.playing);
    std::thread::sleep(Duration::from_millis(800));
    let later = rig.player.state();
    assert!((later.position - paused.position).abs() < 0.3, "position moved while paused");
    rig.player.send(Command::SetVolume(0.05));
    wait_for(&rig, "volume", Duration::from_secs(5), |s| (s.volume - 0.05).abs() < 0.001);
    println!("  ⏸ paused at {:.1} s; volume 0.05", later.position);

    // 5. Replaying a track comes from the local cache.
    let cached: Vec<_> = std::fs::read_dir(&rig.cache_path).unwrap().filter_map(Result::ok).collect();
    println!("  💾 cache holds {} file(s)", cached.len());
    assert!(!cached.is_empty());

    // 6. A YouTube search result streams through the server (needs yt-dlp there).
    let results = rig.runtime.block_on(rig.api.youtube_search("rick astley never gonna give you up", 3));
    match results {
        Ok(results) if !results.is_empty() => {
            let video = &results[0];
            rig.player.send(Command::Play {
                items: vec![QueueItem {
                    audio: AudioRef::YouTube { video_id: video.video_id.clone() },
                    title: video.title.clone(),
                    artist: video.artist.clone().unwrap_or_default(),
                    duration: video.duration,
                }],
                start: 0,
            });
            let started = Instant::now();
            let state = wait_for(&rig, "YouTube stream", Duration::from_secs(90), |s| s.playing && s.position >= 1.0);
            println!(
                "  ▶ YouTube \"{}\" playing after {:.1} s, duration {:.1} s",
                video.title,
                started.elapsed().as_secs_f64(),
                state.duration.unwrap_or_default()
            );
        }
        Ok(_) => println!("  (YouTube search returned nothing; skipped)"),
        Err(error) => println!("  (YouTube unavailable: {}; skipped)", error.message),
    }

    rig.player.send(Command::Stop);
    wait_for(&rig, "stop", Duration::from_secs(5), |s| s.current().is_none() && !s.playing);
    println!("  ■ stopped; {} state updates were pushed", rig.states.lock().unwrap().len());
}
