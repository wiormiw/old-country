//! Pieces shared by every screen: navigation, the now-playing bar, notices, the playlist picker.

use leptos::prelude::*;
use leptos_router::components::A;
use shared::{AppError, RepeatMode};
use shared::timespan::display;

use crate::ipc;
use crate::state::{use_notice, use_player};

#[component]
pub fn Sidebar() -> impl IntoView {
    view! {
        <nav class="sidebar">
            <div class="brand">
                <span class="brand-mark" aria-hidden="true"></span>
                <span>"old-country"</span>
            </div>
            <A href="/">"Library"</A>
            <A href="/playlists">"Playlists"</A>
            <A href="/youtube">"YouTube Music"</A>
            <A href="/downloads">"Downloads"</A>
            <div class="sidebar-spacer"></div>
            <A href="/settings">"Settings"</A>
        </nav>
    }
}

/// Shows a command error. Points to Settings when the server isn't set up or reachable.
#[component]
pub fn ErrorView(error: AppError) -> impl IntoView {
    let needs_settings = matches!(error.code.as_str(), "server.not_configured" | "server.unreachable" | "server.bad_address");
    view! {
        <div class="error-box" role="alert">
            <p>{error.message.clone()}</p>
            {needs_settings.then(|| view! { <A href="/settings">"Open Settings"</A> })}
        </div>
    }
}

#[component]
pub fn Loading() -> impl IntoView {
    view! { <p class="muted">"Loading…"</p> }
}

#[component]
pub fn NoticeBar() -> impl IntoView {
    let notice = use_notice();
    // Notices fade after a few seconds.
    Effect::new(move |_| {
        if notice.0.get().is_some() {
            set_timeout(move || notice.0.set(None), std::time::Duration::from_secs(5));
        }
    });
    move || {
        notice.0.get().map(|(is_error, message)| {
            view! {
                <div class=if is_error { "notice notice-error" } else { "notice" } role="status">
                    <span>{message}</span>
                    <button class="icon" on:click=move |_| notice.0.set(None) aria-label="Dismiss">"✕"</button>
                </div>
            }
        })
    }
}

const SEEK_STEP_SECONDS: f64 = 5.0;
const VOLUME_STEP: f32 = 0.05;
/// Pixels of wheel movement per step. A mouse notch moves at least this far, so each notch is one
/// step; a touchpad's many small movements add up to a step every this many pixels.
const WHEEL_STEP_PIXELS: f64 = 50.0;

/// Turns a wheel event into whole steps: +1 for up (away from you), -1 for down, `None` while a
/// touchpad's small movements are still adding up. `pending` carries the partial movement.
fn wheel_steps(pending: StoredValue<f64>, ev: &leptos::ev::WheelEvent) -> Option<i32> {
    let pixels = match ev.delta_mode() {
        1 => ev.delta_y() * 33.0, // lines
        2 => ev.delta_y() * 400.0, // pages
        _ => ev.delta_y(),
    };
    if pixels == 0.0 {
        return None;
    }
    // Wheel down is a positive delta; a step "up" means more volume or further ahead.
    let direction = |delta: f64| if delta < 0.0 { 1 } else { -1 };
    if pixels.abs() >= WHEEL_STEP_PIXELS {
        pending.set_value(0.0);
        return Some(direction(pixels));
    }
    let total = pending.get_value() + pixels;
    if total.abs() >= WHEEL_STEP_PIXELS {
        pending.set_value(0.0);
        Some(direction(total))
    } else {
        pending.set_value(total);
        None
    }
}

#[component]
pub fn NowPlaying() -> impl IntoView {
    let player = use_player().0;
    let show_queue = RwSignal::new(false);
    // While a slider is dragged, show the dragged value instead of the pushed state.
    let seeking = RwSignal::new(None::<f64>);
    let volume_drag = RwSignal::new(None::<f32>);

    let current = Memo::new(move |_| player.with(|s| s.current().cloned()));
    let position = move || seeking.get().unwrap_or_else(|| player.with(|s| s.position));
    let duration = move || player.with(|s| s.duration.unwrap_or(0.0));
    let volume = move || volume_drag.get().unwrap_or_else(|| player.with(|s| s.volume));

    // The mouse wheel over a slider: up raises the volume or skips ahead, down the opposite.
    let seek_wheel = StoredValue::new(0.0);
    let volume_wheel = StoredValue::new(0.0);
    let on_seek_wheel = move |ev: leptos::ev::WheelEvent| {
        let Some(steps) = wheel_steps(seek_wheel, &ev) else { return };
        let (current, now, length) = player.with_untracked(|s| (s.current().is_some(), s.position, s.duration.unwrap_or(0.0)));
        if current && length > 0.0 {
            let target = (now + f64::from(steps) * SEEK_STEP_SECONDS).clamp(0.0, length);
            leptos::task::spawn_local(ipc::seek(target));
        }
    };
    let on_volume_wheel = move |ev: leptos::ev::WheelEvent| {
        let Some(steps) = wheel_steps(volume_wheel, &ev) else { return };
        let level = (player.with_untracked(|s| s.volume) + steps as f32 * VOLUME_STEP).clamp(0.0, 1.0);
        leptos::task::spawn_local(ipc::set_volume(level, true));
    };

    view! {
        <footer class="now-playing">
            <div class="np-track">
                {move || match current.get() {
                    Some(item) => view! {
                        <div class="np-title">{item.title}</div>
                        <div class="np-artist">{item.artist}</div>
                    }.into_any(),
                    None => view! { <div class="np-title muted">"Nothing playing"</div> }.into_any(),
                }}
                {move || player.with(|s| s.error.clone()).map(|e| view! { <div class="np-error">{e}</div> })}
            </div>

            <div class="np-center">
                <div class="np-controls">
                    <button class="icon toggle" aria-label="Shuffle"
                        title=move || if player.with(|s| s.shuffle) { "Shuffle on: every song once before any repeats" } else { "Shuffle off" }
                        class:active=move || player.with(|s| s.shuffle)
                        on:click=move |_| {
                            let on = !player.with_untracked(|s| s.shuffle);
                            leptos::task::spawn_local(ipc::set_shuffle(on));
                        }>"⇄"</button>
                    <button class="icon" aria-label="Previous" on:click=move |_| leptos::task::spawn_local(ipc::prev())>"⏮"</button>
                    <button class="icon play" aria-label="Play or pause" on:click=move |_| leptos::task::spawn_local(ipc::toggle())>
                        {move || if player.with(|s| s.buffering) { "…" } else if player.with(|s| s.playing) { "⏸" } else { "▶" }}
                    </button>
                    <button class="icon" aria-label="Next" on:click=move |_| leptos::task::spawn_local(ipc::next())>"⏭"</button>
                    <button class="icon toggle" aria-label="Repeat"
                        title={move || match player.with(|s| s.repeat) {
                            RepeatMode::Off => "Repeat off",
                            RepeatMode::All => "Repeat all",
                            RepeatMode::One => "Repeat this song",
                        }}
                        class:active=move || player.with(|s| s.repeat) != RepeatMode::Off
                        on:click=move |_| {
                            let mode = player.with_untracked(|s| s.repeat).next();
                            leptos::task::spawn_local(ipc::set_repeat(mode));
                        }>
                        {move || if player.with(|s| s.repeat) == RepeatMode::One { "↻1" } else { "↻" }}
                    </button>
                </div>
                <div class="np-seek">
                    <span class="time">{move || display(position())}</span>
                    <input
                        type="range"
                        min="0"
                        step="0.5"
                        aria-label="Seek"
                        title="Drag, or scroll to skip 5 seconds"
                        on:wheel=on_seek_wheel
                        prop:max=move || duration().max(0.5)
                        prop:value=position
                        prop:disabled=move || current.get().is_none()
                        on:input=move |ev| seeking.set(event_target_value(&ev).parse().ok())
                        on:change=move |ev| {
                            if let Ok(seconds) = event_target_value(&ev).parse::<f64>() {
                                leptos::task::spawn_local(ipc::seek(seconds));
                            }
                            seeking.set(None);
                        }
                    />
                    <span class="time">{move || display(duration())}</span>
                </div>
            </div>

            <div class="np-right">
                <input
                    type="range"
                    class="volume"
                    min="0"
                    max="1"
                    step="0.01"
                    aria-label="Volume"
                    title={move || format!("Volume {}% (scroll to change)", (volume() * 100.0).round())}
                    on:wheel=on_volume_wheel
                    prop:value=volume
                    on:input=move |ev| {
                        if let Ok(v) = event_target_value(&ev).parse::<f32>() {
                            volume_drag.set(Some(v));
                            leptos::task::spawn_local(ipc::set_volume(v, false));
                        }
                    }
                    on:change=move |ev| {
                        if let Ok(v) = event_target_value(&ev).parse::<f32>() {
                            leptos::task::spawn_local(ipc::set_volume(v, true));
                        }
                        volume_drag.set(None);
                    }
                />
                <button class="ghost" on:click=move |_| show_queue.update(|s| *s = !*s)>
                    {move || format!("Queue ({})", player.with(|s| s.queue.len()))}
                </button>
            </div>
        </footer>
        <Show when=move || show_queue.get()>
            <QueueDrawer on_close=move || show_queue.set(false) />
        </Show>
    }
}

#[component]
fn QueueDrawer(on_close: impl Fn() + 'static + Copy) -> impl IntoView {
    let player = use_player().0;
    view! {
        <aside class="drawer" aria-label="Play queue">
            <header class="drawer-head">
                <h2>"Queue"</h2>
                <button class="icon" aria-label="Close" on:click=move |_| on_close()>"✕"</button>
            </header>
            {move || {
                let state = player.get();
                if state.queue.is_empty() {
                    return view! { <p class="muted">"The queue is empty. Play something from the library."</p> }.into_any();
                }
                // In play order (shuffled when shuffle is on); songs before the current one have
                // been heard this cycle.
                let current_at = state.index.and_then(|i| state.order.iter().position(|&o| o == i));
                view! {
                    <ol class="queue">
                        {state.order.iter().enumerate().map(|(at, &i)| {
                            let item = state.queue[i].clone();
                            let current = state.index == Some(i);
                            let heard = current_at.is_some_and(|c| at < c);
                            view! {
                                <li class:current=current class:heard=heard>
                                    <button class="queue-item" on:click=move |_| leptos::task::spawn_local(ipc::jump(i))>
                                        <span class="q-title">{item.title}</span>
                                        <span class="q-artist">{item.artist}</span>
                                    </button>
                                    <button class="icon" aria-label="Remove from queue"
                                        on:click=move |_| leptos::task::spawn_local(ipc::remove_from_queue(i))>"✕"</button>
                                </li>
                            }
                        }).collect_view()}
                    </ol>
                }.into_any()
            }}
        </aside>
    }
}

/// Which tracks the playlist picker is adding; `None` hides it.
#[derive(Clone, Copy)]
pub struct PickerTarget(pub RwSignal<Option<Vec<String>>>);

pub fn provide_picker() {
    provide_context(PickerTarget(RwSignal::new(None)));
}

pub fn open_picker(track_ids: Vec<String>) {
    expect_context::<PickerTarget>().0.set(Some(track_ids));
}

#[component]
pub fn PlaylistPicker() -> impl IntoView {
    let target = expect_context::<PickerTarget>().0;
    let notice = use_notice();
    let playlists = LocalResource::new(move || async move {
        // Only fetch when the picker opens.
        if target.with(Option::is_some) { Some(ipc::list_playlists().await) } else { None }
    });

    let add = move |id: String, name: String| {
        let Some(track_ids) = target.get_untracked() else { return };
        target.set(None);
        leptos::task::spawn_local(async move {
            match ipc::add_playlist_tracks(id, track_ids).await {
                Ok(summary) => notice.info(format!("Added to {name} ({} tracks).", summary.track_count)),
                Err(error) => notice.error(error.message),
            }
        });
    };

    view! {
        <Show when=move || target.with(Option::is_some)>
            <div class="modal-backdrop" on:click=move |_| target.set(None)>
                <div class="modal" role="dialog" aria-label="Add to playlist" on:click=|ev| ev.stop_propagation()>
                    <h2>"Add to playlist"</h2>
                    {move || match playlists.get().flatten() {
                        None => view! { <Loading /> }.into_any(),
                        Some(Err(error)) => view! { <ErrorView error /> }.into_any(),
                        Some(Ok(list)) if list.is_empty() => view! {
                            <p class="muted">"No playlists yet. Create one on the Playlists page."</p>
                        }.into_any(),
                        Some(Ok(list)) => view! {
                            <ul class="picker">
                                {list.into_iter().map(|p| {
                                    let (id, name) = (p.id.clone(), p.name.clone());
                                    view! {
                                        <li><button on:click=move |_| add(id.clone(), name.clone())>
                                            <span>{p.name.clone()}</span>
                                            <span class="muted">{format!("{} tracks", p.track_count)}</span>
                                        </button></li>
                                    }
                                }).collect_view()}
                            </ul>
                        }.into_any(),
                    }}
                    <button class="ghost" on:click=move |_| target.set(None)>"Cancel"</button>
                </div>
            </div>
        </Show>
    }
}
