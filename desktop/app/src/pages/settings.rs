use leptos::prelude::*;
use shared::{AppError, Settings};

use crate::ipc;
use crate::state::use_notice;

#[component]
pub fn SettingsPage() -> impl IntoView {
    let notice = use_notice();
    let server = RwSignal::new(String::new());
    let cache_mb = RwSignal::new(String::from("2048"));
    let loaded = RwSignal::new(None::<Settings>);
    let error = RwSignal::new(None::<AppError>);
    let checking = RwSignal::new(false);

    leptos::task::spawn_local(async move {
        if let Ok(settings) = ipc::get_settings().await {
            server.set(settings.server_url.clone());
            cache_mb.set(settings.cache_max_megabytes.to_string());
            loaded.set(Some(settings));
        }
    });

    let check = move |_| {
        checking.set(true);
        leptos::task::spawn_local(async move {
            match ipc::check_server(server.get_untracked()).await {
                Ok(()) => {
                    error.set(None);
                    notice.info("The server is reachable and healthy.");
                }
                Err(e) => error.set(Some(e)),
            }
            checking.set(false);
        });
    };

    let save = move |ev: leptos::ev::SubmitEvent| {
        ev.prevent_default();
        let Some(mut settings) = loaded.get_untracked() else { return };
        settings.server_url = server.get_untracked();
        settings.cache_max_megabytes = cache_mb.get_untracked().trim().parse().unwrap_or(settings.cache_max_megabytes);
        checking.set(true);
        leptos::task::spawn_local(async move {
            match ipc::save_settings(settings).await {
                Ok(saved) => {
                    error.set(None);
                    server.set(saved.server_url.clone());
                    cache_mb.set(saved.cache_max_megabytes.to_string());
                    loaded.set(Some(saved));
                    notice.info("Settings saved.");
                }
                Err(e) => error.set(Some(e)),
            }
            checking.set(false);
        });
    };

    view! {
        <section class="page">
            <header class="page-head"><h1>"Settings"</h1></header>
            <form class="stack-form" on:submit=save>
                <label>
                    "Server address"
                    <input type="text" placeholder="http://192.168.1.10:5000" bind:value=server />
                    <span class="hint">"Where the old-country API runs. Saving checks that it answers."</span>
                </label>
                <label>
                    "Audio cache size (MB)"
                    <input type="number" min="128" step="128" bind:value=cache_mb />
                    <span class="hint">"Recently played audio is kept for instant replays and offline listening."</span>
                </label>
                {move || error.get().map(|e| view! { <p class="field-error" role="alert">{e.message}</p> })}
                <div class="actions">
                    <button class="primary" type="submit" prop:disabled=move || checking.get() || loaded.with(Option::is_none)>
                        {move || if checking.get() { "Checking…" } else { "Save" }}
                    </button>
                    <button class="ghost" type="button" on:click=check prop:disabled=move || checking.get()>"Test connection"</button>
                </div>
            </form>
        </section>
    }
}
