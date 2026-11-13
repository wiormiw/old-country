//! The first page: a welcome, what's playing, where the server stands, and the way to everything.

use leptos::prelude::*;
use leptos_router::components::A;

use crate::components::{Art, ThemeToggle};
use crate::ipc;
use crate::state::{time_ago, use_player};

/// How many playlists the Home page offers to jump back into.
const RECENT_PLAYLISTS: usize = 6;

/// "Good evening" and friends, by the computer's clock.
fn greeting() -> &'static str {
    match js_sys::Date::new_0().get_hours() {
        5..=11 => "Good morning",
        12..=17 => "Good afternoon",
        18..=22 => "Good evening",
        _ => "Up late",
    }
}

#[component]
pub fn Home() -> impl IntoView {
    let player = use_player();
    let playlists = LocalResource::new(ipc::list_playlists);
    let songs = LocalResource::new(|| ipc::list_tracks(1, 1, None));
    // The server's state, checked once on opening: Some(Ok(address)) or Some(Err(why)).
    let server = LocalResource::new(|| async {
        let settings = ipc::get_settings().await?;
        ipc::check_server(settings.server_url.clone()).await.map(|()| settings.server_url)
    });

    let current = Memo::new(move |_| player.0.with(|s| s.current().cloned()));

    view! {
        <section class="page home">
            <header class="home-head">
                <div>
                    <p class="eyebrow">"Welcome home"</p>
                    <h1 class="home-greeting">{greeting()}</h1>
                    <p class="muted">
                        {move || match songs.get() {
                            Some(Ok(page)) => format!("{} songs in your library.", page.total_count),
                            _ => "Your library and YouTube Music, in one place.".to_owned(),
                        }}
                    </p>
                </div>
                <div class="home-tools">
                    <ThemeToggle />
                    <A href="/settings" attr:class="icon-link" attr:title="Settings" attr:aria-label="Settings">"⚙"</A>
                </div>
            </header>

            {move || match server.get() {
                None => view! { <p class="server-line muted">"Checking the server…"</p> }.into_any(),
                Some(Ok(address)) => view! {
                    <p class="server-line"><span class="dot dot-ok"></span>{format!("Connected to {address}")}</p>
                }.into_any(),
                Some(Err(error)) => view! {
                    <p class="server-line">
                        <span class="dot dot-off"></span>{error.message}" "
                        <A href="/settings">"Open Settings"</A>
                    </p>
                }.into_any(),
            }}

            {move || current.get().map(|item| view! {
                <div class="home-now appear">
                    <Art video_id=item.video_id.clone() large=true />
                    <div class="home-now-text">
                        <p class="eyebrow">{move || if player.0.with(|s| s.playing) { "Now playing" } else { "Paused" }}</p>
                        <p class="home-now-title">{item.title.clone()}</p>
                        <p class="muted">{item.artist.clone()}</p>
                        <button class="primary" on:click=move |_| leptos::task::spawn_local(ipc::toggle())>
                            {move || if player.0.with(|s| s.playing) { "❚❚ Pause" } else { "▶ Play" }}
                        </button>
                    </div>
                </div>
            })}

            <nav class="home-shortcuts" aria-label="Go to">
                <A href="/library"><span class="shortcut-icon">"♫"</span><span>"Library"</span></A>
                <A href="/playlists"><span class="shortcut-icon">"☰"</span><span>"Playlists"</span></A>
                <A href="/youtube"><span class="shortcut-icon">"⌕"</span><span>"YouTube Music"</span></A>
                <A href="/downloads"><span class="shortcut-icon">"⤓"</span><span>"Downloads"</span></A>
            </nav>

            {move || match playlists.get() {
                Some(Ok(mut list)) if !list.is_empty() => {
                    // Most recently changed first.
                    list.sort_by(|a, b| b.updated_at.as_ref().unwrap_or(&b.created_at).cmp(a.updated_at.as_ref().unwrap_or(&a.created_at)));
                    list.truncate(RECENT_PLAYLISTS);
                    view! {
                        <h2 class="home-section">"Jump back in"</h2>
                        <ul class="cards appear">
                            {list.into_iter().map(|p| view! {
                                <li>
                                    <A href=format!("/playlists/{}", p.id)>
                                        <span class="card-title">{p.name.clone()}</span>
                                        <span class="muted">
                                            {format!("{} songs", p.track_count)}
                                            {p.updated_at.as_deref().map(|at| format!(" · changed {}", time_ago(at)))}
                                        </span>
                                    </A>
                                </li>
                            }).collect_view()}
                        </ul>
                    }.into_any()
                }
                _ => ().into_any(),
            }}
        </section>
    }
}
