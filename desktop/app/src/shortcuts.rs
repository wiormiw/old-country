//! Keyboard shortcuts for the whole window, like in Spotify:
//!
//! | Keys            | Action                         |
//! |-----------------|--------------------------------|
//! | Space           | Play or pause                  |
//! | Ctrl+→ / Ctrl+← | Next / previous track          |
//! | Ctrl+↑ / Ctrl+↓ | Volume up / down (10 %)        |
//! | Ctrl+S          | Shuffle on or off              |
//! | Ctrl+R          | The next repeat mode           |
//! | Ctrl+L          | Search: the box of the page, or YouTube search |
//!
//! Cmd works in place of Ctrl, for macOS. No shortcut works while you type in a text field, so there
//! Space types a space and Ctrl+←/→ moves the cursor by a word.
//!
//! Without these, Space only did the default of the browser engine: it clicked the button with the
//! focus. WebKitGTK keeps the focus on a button after a click, so after "Play all" a later Space
//! started the playlist again from the first track.

use leptos::ev;
use leptos::prelude::*;
use leptos::wasm_bindgen::JsCast;
use leptos_router::hooks::use_navigate;

use crate::ipc;
use crate::state::use_player;

/// The change of volume for each key press.
const VOLUME_STEP: f32 = 0.1;

/// What has the keyboard focus, as far as the shortcuts are concerned.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Focus {
    /// Typing goes here: a text input, a text area, a select, editable content. No shortcuts here.
    TextField,
    /// A button, a link or a checkbox: Space would click it.
    Button,
    /// A range input (seek bar, volume).
    Slider,
    /// The page, or anything else.
    Other,
}

/// The keys held with the key.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Modifiers {
    /// Ctrl, or Cmd on macOS.
    pub primary: bool,
    pub shift: bool,
    pub alt: bool,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Shortcut {
    PlayPause,
    Next,
    Previous,
    VolumeUp,
    VolumeDown,
    Shuffle,
    Repeat,
    Search,
}

/// The shortcut for a key (the `key` value of a KeyboardEvent), or `None`.
pub fn shortcut_for(key: &str, modifiers: Modifiers, focus: Focus) -> Option<Shortcut> {
    if focus == Focus::TextField || modifiers.alt || modifiers.shift {
        return None;
    }
    if !modifiers.primary {
        return (key == " ").then_some(Shortcut::PlayPause);
    }
    match key {
        "ArrowRight" => Some(Shortcut::Next),
        "ArrowLeft" => Some(Shortcut::Previous),
        "ArrowUp" => Some(Shortcut::VolumeUp),
        "ArrowDown" => Some(Shortcut::VolumeDown),
        "s" | "S" => Some(Shortcut::Shuffle),
        "r" | "R" => Some(Shortcut::Repeat),
        "l" | "L" => Some(Shortcut::Search),
        _ => None,
    }
}

/// The new volume after a key press, from 0.0 to 1.0.
pub fn stepped_volume(volume: f32, up: bool) -> f32 {
    let next = if up { volume + VOLUME_STEP } else { volume - VOLUME_STEP };
    // Round to the step, so ten presses from 0 reach exactly 1.
    ((next / VOLUME_STEP).round() * VOLUME_STEP).clamp(0.0, 1.0)
}

/// Listens on the window for the shortcuts. Put it one time inside the router, because Ctrl+L can
/// open the search page.
#[component]
pub fn Shortcuts() -> impl IntoView {
    let player = use_player().0;
    let navigate = use_navigate();
    let read = |event: &web_sys::KeyboardEvent| {
        let modifiers = Modifiers {
            primary: event.ctrl_key() || event.meta_key(),
            shift: event.shift_key(),
            alt: event.alt_key(),
        };
        shortcut_for(&event.key(), modifiers, focus_of(event))
    };

    let keydown = window_event_listener(ev::keydown, move |event| {
        let Some(shortcut) = read(&event) else { return };
        event.prevent_default();
        // A held key repeats volume steps and track changes, but toggles only one time.
        if event.repeat() && matches!(shortcut, Shortcut::PlayPause | Shortcut::Shuffle | Shortcut::Repeat | Shortcut::Search) {
            return;
        }
        match shortcut {
            Shortcut::PlayPause => leptos::task::spawn_local(ipc::toggle()),
            Shortcut::Next => leptos::task::spawn_local(ipc::next()),
            Shortcut::Previous => leptos::task::spawn_local(ipc::prev()),
            Shortcut::VolumeUp | Shortcut::VolumeDown => {
                let volume = stepped_volume(player.with_untracked(|s| s.volume), shortcut == Shortcut::VolumeUp);
                leptos::task::spawn_local(ipc::set_volume(volume, true));
            }
            Shortcut::Shuffle => {
                let on = !player.with_untracked(|s| s.shuffle);
                leptos::task::spawn_local(ipc::set_shuffle(on));
            }
            Shortcut::Repeat => {
                let mode = player.with_untracked(|s| s.repeat).next();
                leptos::task::spawn_local(ipc::set_repeat(mode));
            }
            Shortcut::Search => {
                if !focus_search_box() {
                    navigate("/youtube", Default::default());
                    // The page draws after this handler. Focus its box on the next frame.
                    request_animation_frame(|| {
                        focus_search_box();
                    });
                }
            }
        }
    });
    // WebKit clicks a focused button on key up, so key up must also stop the default for Space.
    let keyup = window_event_listener(ev::keyup, move |event| {
        if read(&event) == Some(Shortcut::PlayPause) {
            event.prevent_default();
        }
    });
    on_cleanup(move || {
        keydown.remove();
        keyup.remove();
    });
}

/// Focuses the search box of the current page. False when the page has none.
fn focus_search_box() -> bool {
    let found = document()
        .query_selector("main input[type=search]")
        .ok()
        .flatten()
        .and_then(|element| element.dyn_into::<web_sys::HtmlElement>().ok());
    match found {
        Some(input) => input.focus().is_ok(),
        None => false,
    }
}

fn focus_of(event: &web_sys::KeyboardEvent) -> Focus {
    let Some(element) = event.target().and_then(|target| target.dyn_into::<web_sys::Element>().ok()) else {
        return Focus::Other;
    };
    if element.get_attribute("contenteditable").is_some_and(|value| value != "false") {
        return Focus::TextField;
    }
    match element.tag_name().to_ascii_uppercase().as_str() {
        "TEXTAREA" | "SELECT" => Focus::TextField,
        "BUTTON" | "A" => Focus::Button,
        "INPUT" => match element.get_attribute("type").unwrap_or_default().to_ascii_lowercase().as_str() {
            "range" => Focus::Slider,
            "checkbox" | "radio" | "button" | "submit" | "reset" => Focus::Button,
            _ => Focus::TextField,
        },
        _ => Focus::Other,
    }
}

#[cfg(test)]
mod tests {
    use super::{Focus, Modifiers, Shortcut, shortcut_for, stepped_volume};

    const NONE: Modifiers = Modifiers { primary: false, shift: false, alt: false };
    const CTRL: Modifiers = Modifiers { primary: true, shift: false, alt: false };

    #[test]
    fn space_plays_or_pauses_wherever_the_focus_is() {
        // After a click on "Play all", that button keeps the focus. Space must not click it again.
        assert_eq!(shortcut_for(" ", NONE, Focus::Button), Some(Shortcut::PlayPause));
        assert_eq!(shortcut_for(" ", NONE, Focus::Other), Some(Shortcut::PlayPause));
        assert_eq!(shortcut_for(" ", NONE, Focus::Slider), Some(Shortcut::PlayPause));
    }

    #[test]
    fn the_spotify_shortcuts_work_with_ctrl() {
        assert_eq!(shortcut_for("ArrowRight", CTRL, Focus::Other), Some(Shortcut::Next));
        assert_eq!(shortcut_for("ArrowLeft", CTRL, Focus::Button), Some(Shortcut::Previous));
        assert_eq!(shortcut_for("ArrowUp", CTRL, Focus::Slider), Some(Shortcut::VolumeUp));
        assert_eq!(shortcut_for("ArrowDown", CTRL, Focus::Other), Some(Shortcut::VolumeDown));
        assert_eq!(shortcut_for("s", CTRL, Focus::Other), Some(Shortcut::Shuffle));
        assert_eq!(shortcut_for("R", CTRL, Focus::Other), Some(Shortcut::Repeat));
        assert_eq!(shortcut_for("l", CTRL, Focus::Other), Some(Shortcut::Search));
    }

    #[test]
    fn nothing_happens_while_you_type() {
        // Space types a space; Ctrl+arrows move the cursor by a word.
        assert_eq!(shortcut_for(" ", NONE, Focus::TextField), None);
        assert_eq!(shortcut_for("ArrowLeft", CTRL, Focus::TextField), None);
        assert_eq!(shortcut_for("l", CTRL, Focus::TextField), None);
    }

    #[test]
    fn other_keys_and_combinations_are_not_shortcuts() {
        assert_eq!(shortcut_for("Enter", NONE, Focus::Button), None);
        assert_eq!(shortcut_for("ArrowRight", NONE, Focus::Other), None);
        assert_eq!(shortcut_for("s", NONE, Focus::Other), None);
        assert_eq!(shortcut_for(" ", CTRL, Focus::Other), None);
        assert_eq!(shortcut_for("ArrowRight", Modifiers { shift: true, ..CTRL }, Focus::Other), None);
        assert_eq!(shortcut_for(" ", Modifiers { alt: true, ..NONE }, Focus::Other), None);
    }

    #[test]
    fn volume_moves_in_steps_of_ten_percent_and_stays_in_range() {
        assert!((stepped_volume(0.5, true) - 0.6).abs() < 1e-6);
        assert!((stepped_volume(0.53, false) - 0.4).abs() < 1e-6);
        assert!((stepped_volume(0.95, true) - 1.0).abs() < 1e-6);
        assert_eq!(stepped_volume(0.05, false), 0.0);
        assert_eq!(stepped_volume(1.0, true), 1.0);
    }
}
