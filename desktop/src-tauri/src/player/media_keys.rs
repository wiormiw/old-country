//! The desktop's media controls (MPRIS on Linux, via souvlaki): KDE's panel widget and keyboard
//! media keys drive the player, and show what's playing, even while the window is minimised.

use std::sync::Mutex;
use std::time::Duration;

use shared::{PlayerState, QueueItem};
use souvlaki::{MediaControlEvent, MediaControls, MediaMetadata, MediaPlayback, MediaPosition, PlatformConfig, SeekDirection};

use super::{Command, PlayerHandle};

/// How far the "seek" media keys jump.
const SEEK_STEP: f64 = 10.0;

/// A position change bigger than this between two state updates is a seek, not playback moving
/// on (updates come every 250 ms while playing).
const SEEK_JUMP: f64 = 1.5;

pub struct MediaKeys {
    controls: Mutex<MediaControls>,
    shown: Mutex<Shown>,
}

/// What the desktop was last told.
#[derive(Default)]
struct Shown {
    item: Option<QueueItem>,
    playback: Option<(bool, f64)>,
}

/// Whether the desktop needs a new playback status. Only for real changes: souvlaki's D-Bus thread
/// handles about one update a second, so sending every position tick (4 a second) queued them
/// up, and the desktop lagged further behind the longer a song played (showing "playing" long
/// after a pause). Desktops move the position on by themselves while it's playing.
fn playback_changed(last: Option<(bool, f64)>, playing: bool, position: f64) -> bool {
    match last {
        None => true,
        Some((was_playing, _)) if was_playing != playing => true,
        Some((_, was_at)) => {
            let moved = position - was_at;
            moved < 0.0 || moved > SEEK_JUMP
        }
    }
}

impl MediaKeys {
    /// Registers with the desktop. `None` (logged) when there's no session bus, e.g. headless.
    /// `hwnd` is the main window's handle, which Windows needs (souvlaki panics without one there).
    pub fn start(player: PlayerHandle, hwnd: Option<*mut std::ffi::c_void>) -> Option<Self> {
        if cfg!(target_os = "windows") && hwnd.is_none() {
            log::warn!("media keys unavailable: no window handle");
            return None;
        }
        let config = PlatformConfig { display_name: "old-country", dbus_name: "old_country", hwnd };
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
        Some(Self { controls: Mutex::new(controls), shown: Mutex::new(Shown::default()) })
    }

    /// Mirrors the player state to the desktop's media controls.
    pub fn update(&self, state: &PlayerState) {
        let mut controls = self.controls.lock().expect("media controls lock");
        let current = state.current().cloned();
        let mut shown = self.shown.lock().expect("media shown lock");
        let new_item = shown.item != current;
        if new_item {
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
            shown.item.clone_from(&current);
        }

        let playing = state.playing && current.is_some();
        let changed = new_item || playback_changed(shown.playback, playing, state.position);
        // Kept on every update, so the next one compares against now, not the last change.
        shown.playback = Some((playing, state.position));
        if !changed {
            return;
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

#[cfg(test)]
mod tests {
    use super::playback_changed;

    #[test]
    fn playback_moving_on_is_not_a_change() {
        assert!(!playback_changed(Some((true, 10.0)), true, 10.25));
        assert!(!playback_changed(Some((false, 10.0)), false, 10.0));
    }

    #[test]
    fn pausing_resuming_and_seeking_are_changes() {
        assert!(playback_changed(None, true, 0.0));
        assert!(playback_changed(Some((true, 10.0)), false, 10.25));
        assert!(playback_changed(Some((false, 10.0)), true, 10.0));
        assert!(playback_changed(Some((true, 10.0)), true, 60.0)); // seek forward
        assert!(playback_changed(Some((true, 60.0)), true, 5.0)); // seek back or restart
    }
}
