//! The desktop's media controls (MPRIS on Linux, via souvlaki): KDE's panel widget and keyboard
//! media keys drive the player, and show what's playing, even while the window is minimised.

use std::sync::Mutex;
use std::time::Duration;

use shared::{PlayerState, QueueItem};
use souvlaki::{MediaControlEvent, MediaControls, MediaMetadata, MediaPlayback, MediaPosition, PlatformConfig, SeekDirection};

use super::{Command, PlayerHandle};

/// How far the "seek" media keys jump.
const SEEK_STEP: f64 = 10.0;

pub struct MediaKeys {
    controls: Mutex<MediaControls>,
    shown: Mutex<Option<QueueItem>>,
}

impl MediaKeys {
    /// Registers with the desktop. `None` (logged) when there's no session bus, e.g. headless.
    pub fn start(player: PlayerHandle) -> Option<Self> {
        let config = PlatformConfig { display_name: "old-country", dbus_name: "old_country", hwnd: None };
        let mut controls = MediaControls::new(config)
            .map_err(|e| log::warn!("media keys unavailable: {e:?}"))
            .ok()?;
        let result = controls.attach(move |event| match event {
            MediaControlEvent::Play => player.send(Command::Resume),
            MediaControlEvent::Pause => player.send(Command::Pause),
            MediaControlEvent::Toggle => player.send(Command::Toggle),
            MediaControlEvent::Next => player.send(Command::Next),
            MediaControlEvent::Previous => player.send(Command::Prev),
            MediaControlEvent::Stop => player.send(Command::Stop),
            MediaControlEvent::SetPosition(MediaPosition(at)) => player.send(Command::Seek(at.as_secs_f64())),
            MediaControlEvent::Seek(direction) | MediaControlEvent::SeekBy(direction, _) => {
                let now = player.state().position;
                let to = match direction {
                    SeekDirection::Forward => now + SEEK_STEP,
                    SeekDirection::Backward => now - SEEK_STEP,
                };
                player.send(Command::Seek(to.max(0.0)));
            }
            MediaControlEvent::SetVolume(volume) => player.send(Command::SetVolume(volume as f32)),
            _ => {}
        });
        if let Err(error) = result {
            log::warn!("media keys unavailable: {error:?}");
            return None;
        }
        Some(Self { controls: Mutex::new(controls), shown: Mutex::new(None) })
    }

    /// Mirrors the player state to the desktop's media controls.
    pub fn update(&self, state: &PlayerState) {
        let mut controls = self.controls.lock().expect("media controls lock");
        let current = state.current().cloned();
        let mut shown = self.shown.lock().expect("media shown lock");
        if *shown != current {
            let metadata = match &current {
                Some(item) => MediaMetadata {
                    title: Some(&item.title),
                    artist: Some(&item.artist),
                    duration: state.duration.or(item.duration).map(Duration::from_secs_f64),
                    ..Default::default()
                },
                None => MediaMetadata::default(),
            };
            let _ = controls.set_metadata(metadata);
            shown.clone_from(&current);
        }

        let progress = Some(MediaPosition(Duration::from_secs_f64(state.position.max(0.0))));
        let playback = match (&current, state.playing) {
            (None, _) => MediaPlayback::Stopped,
            (Some(_), true) => MediaPlayback::Playing { progress },
            (Some(_), false) => MediaPlayback::Paused { progress },
        };
        let _ = controls.set_playback(playback);
    }
}
