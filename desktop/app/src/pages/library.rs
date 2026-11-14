use leptos::prelude::*;
use shared::LibraryChanges;
use leptos_router::components::A;
use shared::timespan::display;
use shared::{ScanStatus, Track};

use crate::components::{Art, ErrorView, Loading, open_picker};
use crate::ipc;
use crate::state::{on_change, track_item, use_notice};

const PAGE_SIZE: u32 = 50;

#[component]
pub fn Library() -> impl IntoView {
    let page = RwSignal::new(1u32);
    let typed = RwSignal::new(String::new());
    let search = RwSignal::new(String::new());
    // Search 300 ms after typing stops, from the first page.
    Effect::new(move |pending: Option<Option<leptos::prelude::TimeoutHandle>>| {
        let text = typed.get();
        if let Some(Some(handle)) = pending {
            handle.clear();
        }
        set_timeout_with_handle(
            move || {
                if search.get_untracked() != text {
                    search.set(text);
                    page.set(1);
                }
            },
            std::time::Duration::from_millis(300),
        )
        .ok()
    });
    let tracks = LocalResource::new(move || {
        let text = search.get();
        ipc::list_tracks(page.get(), PAGE_SIZE, (!text.trim().is_empty()).then_some(text))
    });
    on_change(LibraryChanges::touches_tracks, move || tracks.refetch());

    view! {
        <section class="page">
            <header class="page-head">
                <h1>"Library"</h1>
                <ScanControl on_finished=move || tracks.refetch() />
            </header>
            <input type="search" class="search" placeholder="Search titles and artists" aria-label="Search the library" bind:value=typed />
            {move || match tracks.get() {
                None => view! { <Loading /> }.into_any(),
                Some(Err(error)) => view! { <ErrorView error /> }.into_any(),
                Some(Ok(paged)) if paged.items.is_empty() && !search.with(|s| s.trim().is_empty()) => view! {
                    <p class="muted">"Nothing matches that search."</p>
                }.into_any(),
                Some(Ok(paged)) if paged.items.is_empty() => view! {
                    <p class="muted">"No tracks yet. Set your music folders on the server (Library:Roots) and scan."</p>
                }.into_any(),
                Some(Ok(paged)) => {
                    let total_pages = paged.total_pages.max(1);
                    view! {
                        <TrackTable tracks=paged.items />
                        <nav class="pager" aria-label="Pages">
                            <button class="ghost" prop:disabled=move || { page.get() <= 1 }
                                on:click=move |_| page.update(|p| *p -= 1)>"← Previous"</button>
                            <span class="muted">{move || format!("Page {} of {total_pages} · {} tracks", page.get(), paged.total_count)}</span>
                            // Braces matter: a bare `>=` in an attribute would end the tag in view!.
                            <button class="ghost" prop:disabled=move || { page.get() >= total_pages }
                                on:click=move |_| page.update(|p| *p += 1)>"Next →"</button>
                        </nav>
                    }.into_any()
                }
            }}
        </section>
    }
}

#[component]
fn TrackTable(tracks: Vec<Track>) -> impl IntoView {
    let queue: Vec<_> = tracks.iter().map(track_item).collect();
    view! {
        <table class="tracks appear">
            <thead>
                <tr><th class="col-num">"#"</th><th>"Title"</th><th class="col-artist">"Artist"</th><th class="col-time">"Time"</th><th class="col-source">"Source"</th><th class="col-actions"></th></tr>
            </thead>
            <tbody>
                {tracks.into_iter().enumerate().map(|(i, track)| {
                    let queue = queue.clone();
                    let id = track.id.clone();
                    let source = match (track.is_available_offline, &track.codec, &track.you_tube_video_id) {
                        (true, Some(codec), _) => codec.clone(),
                        (true, None, _) => "Local".to_owned(),
                        (false, _, Some(_)) => "YouTube".to_owned(),
                        _ => "—".to_owned(),
                    };
                    let row_queue = queue.clone();
                    view! {
                        <tr on:dblclick=move |_| leptos::task::spawn_local(ipc::play(row_queue.clone(), i, None))>
                            <td class="col-num">
                                <button class="icon row-play" aria-label="Play"
                                    on:click=move |_| leptos::task::spawn_local(ipc::play(queue.clone(), i, None))>"▶"</button>
                            </td>
                            <td class="col-title">
                                <div class="title-cell">
                                    <Art video_id=track.you_tube_video_id.clone() />
                                    <div class="title-text">
                                        <A href=format!("/tracks/{}", track.id) attr:class="title-link">{track.title.clone()}</A>
                                        <span class="sub-artist">{track.artist.clone()}</span>
                                    </div>
                                </div>
                            </td>
                            <td class="muted col-artist">{track.artist.clone()}</td>
                            <td class="col-time">{display(track.duration)}</td>
                            <td class="col-source"><span class="badge" class:badge-offline=track.is_available_offline>{source}</span></td>
                            <td class="col-actions">
                                <button class="ghost small" on:click=move |_| open_picker(vec![id.clone()])>"+ Playlist"</button>
                            </td>
                        </tr>
                    }
                }).collect_view()}
            </tbody>
        </table>
    }
}

/// "Scan library" and the server's scan status, polled while a scan runs.
#[component]
fn ScanControl(on_finished: impl Fn() + Copy + 'static) -> impl IntoView {
    let status = RwSignal::new(None::<ScanStatus>);
    let notice = use_notice();

    let refresh = move || {
        leptos::task::spawn_local(async move {
            if let Ok(s) = ipc::scan_status().await {
                let was_busy = status.with_untracked(|old| old.as_ref().is_some_and(|o| o.is_running || o.is_queued));
                let busy = s.is_running || s.is_queued;
                status.set(Some(s));
                if was_busy && !busy {
                    on_finished();
                }
            }
        });
    };
    refresh();
    // Poll every 2 s while the server reports a scan in progress.
    let handle = set_interval_with_handle(
        move || {
            if status.with_untracked(|s| s.as_ref().is_some_and(|s| s.is_running || s.is_queued)) {
                refresh();
            }
        },
        std::time::Duration::from_secs(2),
    )
    .ok();
    on_cleanup(move || {
        if let Some(handle) = handle {
            handle.clear();
        }
    });

    let scan = move |_| {
        leptos::task::spawn_local(async move {
            match ipc::scan_library().await {
                Ok(s) => status.set(Some(s)),
                Err(error) => notice.error(error.message),
            }
        });
    };

    view! {
        <div class="scan">
            <span class="muted">{move || match status.get() {
                Some(s) if s.is_running => "Scanning…".to_owned(),
                Some(s) if s.is_queued => "Scan queued…".to_owned(),
                Some(ScanStatus { last_scan: Some(r), .. }) =>
                    format!("Last scan: {} added, {} known, {} unreadable", r.added, r.already_known, r.unreadable),
                _ => String::new(),
            }}</span>
            <button class="primary" on:click=scan
                aria-busy=move || status.with(|s| s.as_ref().is_some_and(|s| s.is_running || s.is_queued)).to_string()
                prop:disabled=move || status.with(|s| s.as_ref().is_some_and(|s| s.is_running || s.is_queued))>
                "Scan library"
            </button>
        </div>
    }
}
