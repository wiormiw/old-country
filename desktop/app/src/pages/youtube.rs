use leptos::prelude::*;
use leptos_router::components::A;
use shared::timespan::display;
use shared::{AppError, YouTubeResult};

use crate::components::{ErrorView, Loading};
use crate::ipc;
use crate::state::{import_message, use_notice, youtube_item};

const RESULTS: u32 = 25;

#[component]
pub fn YouTube() -> impl IntoView {
    let query = RwSignal::new(String::new());
    let results = RwSignal::new(None::<Result<Vec<YouTubeResult>, AppError>>);
    let searching = RwSignal::new(false);

    let search = move |ev: leptos::ev::SubmitEvent| {
        ev.prevent_default();
        let text = query.get_untracked();
        if text.trim().is_empty() {
            return;
        }
        searching.set(true);
        leptos::task::spawn_local(async move {
            results.set(Some(ipc::youtube_search(text, RESULTS).await));
            searching.set(false);
        });
    };

    view! {
        <section class="page">
            <header class="page-head"><h1>"YouTube Music"</h1></header>
            <form class="inline-form" on:submit=search>
                <input type="search" placeholder="Search songs, artists…" bind:value=query aria-label="Search YouTube" />
                <button class="primary" type="submit" aria-busy=move || searching.get().to_string()
                    prop:disabled=move || searching.get()>
                    {move || if searching.get() { "Searching…" } else { "Search" }}
                </button>
            </form>

            {move || match results.get() {
                None if searching.get() => view! { <Loading /> }.into_any(),
                None => ().into_any(),
                Some(Err(error)) => view! { <ErrorView error /> }.into_any(),
                Some(Ok(list)) if list.is_empty() => view! { <p class="muted">"No results."</p> }.into_any(),
                // Earlier results stay, dimmed, while a new search runs.
                Some(Ok(list)) => view! { <div class="results-wrap" class:stale=move || searching.get()><Results list /></div> }.into_any(),
            }}

            <ImportForm />
        </section>
    }
}

/// A result's download button: not asked yet, asking the server, or queued there.
#[derive(Clone, Copy, PartialEq, Eq)]
enum Download {
    Idle,
    Sending,
    Queued,
}

#[component]
fn Results(list: Vec<YouTubeResult>) -> impl IntoView {
    let notice = use_notice();
    let queue: Vec<_> = list.iter().map(youtube_item).collect();

    view! {
        <ul class="results appear">
            {list.into_iter().enumerate().map(|(i, result)| {
                let item = queue[i].clone();
                let queue = queue.clone();
                let video_id = result.video_id.clone();
                let downloaded = result.is_available_offline;
                let download = RwSignal::new(Download::Idle);
                view! {
                    <li class="result">
                        {result.thumbnail_url.clone().map(|src| view! { <img class="thumb" src=src alt="" loading="lazy" /> })}
                        <div class="result-text">
                            <span class="card-title">{result.title.clone()}</span>
                            <span class="muted">
                                {result.artist.clone().unwrap_or_default()}
                                {result.duration.map(|d| format!(" · {}", display(d)))}
                            </span>
                            <span>
                                {result.track_id.is_some().then(|| view! { <span class="badge">"In library"</span> })}
                                {downloaded.then(|| view! { <span class="badge badge-offline">"Offline"</span> })}
                            </span>
                        </div>
                        <div class="actions">
                            <button class="primary small" on:click=move |_| leptos::task::spawn_local(ipc::play(queue.clone(), i))>"▶ Play"</button>
                            <button class="ghost small" on:click=move |_| {
                                let item = item.clone();
                                leptos::task::spawn_local(async move {
                                    ipc::enqueue(vec![item]).await;
                                    notice.info("Added to the queue.");
                                });
                            }>"+ Queue"</button>
                            <button class="ghost small" aria-busy=move || (download.get() == Download::Sending).to_string()
                                prop:disabled=move || downloaded || download.get() != Download::Idle
                                on:click=move |_| {
                                    let video_id = video_id.clone();
                                    download.set(Download::Sending);
                                    leptos::task::spawn_local(async move {
                                        match ipc::youtube_download(vec![video_id]).await {
                                            Ok(_) => {
                                                download.set(Download::Queued);
                                                notice.info("Download queued. See Downloads for progress.");
                                            }
                                            Err(e) => {
                                                download.set(Download::Idle);
                                                notice.error(e.message);
                                            }
                                        }
                                    });
                                }>
                                {move || match (downloaded, download.get()) {
                                    (true, _) => "Downloaded",
                                    (_, Download::Queued) => "Queued ✓",
                                    _ => "⤓ Download",
                                }}
                            </button>
                        </div>
                    </li>
                }
            }).collect_view()}
        </ul>
    }
}

#[component]
fn ImportForm() -> impl IntoView {
    let playlist = RwSignal::new(String::new());
    let busy = RwSignal::new(false);
    let error = RwSignal::new(None::<AppError>);
    let notice = use_notice();

    let import = move |ev: leptos::ev::SubmitEvent| {
        ev.prevent_default();
        let source = playlist.get_untracked();
        busy.set(true);
        leptos::task::spawn_local(async move {
            match ipc::youtube_import(source).await {
                Ok(report) => {
                    error.set(None);
                    playlist.set(String::new());
                    notice.info(import_message(&report));
                }
                Err(e) => error.set(Some(e)),
            }
            busy.set(false);
        });
    };

    view! {
        <section class="panel">
            <h2>"Import a playlist"</h2>
            <p class="muted">
                "Paste a YouTube Music playlist URL or id, or type " <code>"liked"</code>
                " for your liked songs (needs YouTubeMusic:CookiesFile on the server). It becomes a library playlist under "
                <A href="/playlists">"Playlists"</A> ". Importing the same playlist again adds the songs added on YouTube "
                "since; your renames, reordering and removals are kept."
            </p>
            <form class="inline-form" on:submit=import>
                <input type="text" placeholder="https://music.youtube.com/playlist?list=… or liked" bind:value=playlist aria-label="Playlist to import" />
                <button class="primary" type="submit" aria-busy=move || busy.get().to_string() prop:disabled=move || busy.get()>
                    {move || if busy.get() { "Importing…" } else { "Import" }}
                </button>
            </form>
            {move || error.get().map(|e| view! { <p class="field-error">{e.message}</p> })}
        </section>
    }
}
