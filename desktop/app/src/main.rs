//! old-country desktop UI: Leptos, client-side rendered, compiled to wasm by Trunk and hosted in
//! the Tauri window. All data comes from the desktop core through [`ipc`].

mod components;
mod ipc;
mod pages;
mod state;

use leptos::prelude::*;
use leptos_router::components::{Route, Router, Routes};
use leptos_router::hooks::use_location;
use leptos_router::path;

fn main() {
    console_error_panic_hook::set_once();
    leptos::mount::mount_to_body(App);
}

#[component]
fn App() -> impl IntoView {
    state::provide_app_state();
    components::provide_picker();
    let content = NodeRef::<leptos::html::Main>::new();

    view! {
        <Router>
            <components::RequestBar />
            <div class="shell">
                <components::Sidebar />
                <ScrollToTopOnNavigate content />
                <main class="content" node_ref=content>
                    <Routes fallback=|| view! { <p class="muted">"Page not found."</p> }>
                        <Route path=path!("/") view=pages::home::Home />
                        <Route path=path!("/library") view=pages::library::Library />
                        <Route path=path!("/tracks/:id") view=pages::track::TrackPage />
                        <Route path=path!("/playlists") view=pages::playlists::Playlists />
                        <Route path=path!("/playlists/:id") view=pages::playlists::PlaylistDetail />
                        <Route path=path!("/youtube") view=pages::youtube::YouTube />
                        <Route path=path!("/downloads") view=pages::downloads::Downloads />
                        <Route path=path!("/settings") view=pages::settings::SettingsPage />
                    </Routes>
                </main>
                <components::NowPlaying />
            </div>
            <components::PlaylistPicker />
            <components::NoticeBar />
        </Router>
    }
}

/// A new page starts at its top, not wherever the previous page was scrolled to.
#[component]
fn ScrollToTopOnNavigate(content: NodeRef<leptos::html::Main>) -> impl IntoView {
    let location = use_location();
    Effect::new(move |_| {
        location.pathname.track();
        if let Some(main) = content.get_untracked() {
            main.set_scroll_top(0);
        }
    });
}
