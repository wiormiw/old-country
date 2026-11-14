use leptos::prelude::*;
use leptos_router::components::A;
use leptos_router::hooks::use_params_map;
use shared::TrackDetail;
use shared::timespan::display;

use crate::components::{Art, ErrorView, Loading, open_picker};
use crate::ipc;
use crate::state::{on_change, problem_label, track_item, use_notice};

/// One song: what it is, where it plays from, and the playlists it's in.
#[component]
pub fn TrackPage() -> impl IntoView {
    let params = use_params_map();
    let id = Memo::new(move |_| params.with(|p| p.get("id").unwrap_or_default()));
    let detail = LocalResource::new(move || ipc::get_track(id.get()));
    on_change(|c| c.touches_tracks() || c.touches_playlist_list(), move || detail.refetch());

    view! {
        <section class="page">
            {move || match detail.get() {
                None => view! { <Loading /> }.into_any(),
                Some(Err(error)) => view! { <ErrorView error /> }.into_any(),
                Some(Ok(detail)) => view! { <TrackView detail /> }.into_any(),
            }}
        </section>
    }
}

#[component]
fn TrackView(detail: TrackDetail) -> impl IntoView {
    let notice = use_notice();
    let track = detail.track.clone();
    let item = track_item(&track);
    let youtube_only = !track.is_available_offline && track.you_tube_video_id.is_some();
    let source = match (track.is_available_offline, &track.you_tube_video_id) {
        (true, Some(_)) => "Local file, also on YouTube Music",
        (true, None) => "Local file",
        (false, Some(_)) => "YouTube Music (streams; not downloaded)",
        (false, None) => "Unavailable",
    };

    let play = {
        let item = item.clone();
        move |_| leptos::task::spawn_local(ipc::play(vec![item.clone()], 0, None))
    };
    let enqueue = {
        let item = item.clone();
        move |_| {
            let item = item.clone();
            leptos::task::spawn_local(async move {
                ipc::enqueue(vec![item]).await;
                notice.info("Added to the queue.");
            });
        }
    };
    let add_to_playlist = {
        let id = track.id.clone();
        move |_| open_picker(vec![id.clone()])
    };
    // None: not asked; Some(false): asking the server; Some(true): queued there.
    let queued = RwSignal::new(None::<bool>);
    let download = {
        let video_id = track.you_tube_video_id.clone();
        move |_| {
            let Some(video_id) = video_id.clone() else { return };
            queued.set(Some(false));
            leptos::task::spawn_local(async move {
                match ipc::youtube_download(vec![video_id]).await {
                    Ok(_) => {
                        queued.set(Some(true));
                        notice.info("Download queued. See Downloads for progress.");
                    }
                    Err(e) => {
                        queued.set(None);
                        notice.error(e.message);
                    }
                }
            });
        }
    };

    let quality = match (detail.bitrate_kbps.filter(|b| *b > 0), detail.sample_rate_hz.filter(|r| *r > 0)) {
        (Some(bitrate), Some(rate)) => Some(format!("{bitrate} kbit/s · {:.1} kHz", f64::from(rate) / 1000.0)),
        (Some(bitrate), None) => Some(format!("{bitrate} kbit/s")),
        (None, Some(rate)) => Some(format!("{:.1} kHz", f64::from(rate) / 1000.0)),
        (None, None) => None,
    };

    view! {
        <header class="page-head">
            <Art video_id=track.you_tube_video_id.clone() large=true />
            <div class="page-head-text">
                <p class="eyebrow">"Song"</p>
                <h1>{track.title.clone()}</h1>
                <p class="muted">{format!("{} · {}", track.artist, display(track.duration))}</p>
            </div>
            <div class="actions">
                <button class="primary" on:click=play>"▶ Play"</button>
                <button class="ghost" on:click=enqueue>"+ Queue"</button>
                <button class="ghost" on:click=add_to_playlist>"+ Playlist"</button>
                {youtube_only.then(|| view! {
                    <button class="ghost" on:click=download
                        aria-busy=move || (queued.get() == Some(false)).to_string()
                        prop:disabled=move || queued.get().is_some()>
                        {move || if queued.get() == Some(true) { "Queued ✓" } else { "⤓ Download" }}
                    </button>
                })}
            </div>
        </header>

        <dl class="details">
            <dt>"Plays from"</dt><dd>{source}</dd>
            {track.codec.clone().map(|codec| view! { <dt>"Format"</dt><dd>{codec}</dd> })}
            {quality.map(|q| view! { <dt>"Quality"</dt><dd>{q}</dd> })}
            {detail.size_bytes.map(|bytes| view! { <dt>"File size"</dt><dd>{format!("{:.1} MB", bytes as f64 / 1_048_576.0)}</dd> })}
            {detail.file_name.clone().map(|name| view! { <dt>"File"</dt><dd><code>{name}</code></dd> })}
            {track.you_tube_video_id.clone().map(|id| view! { <dt>"YouTube id"</dt><dd><code>{id}</code></dd> })}
            {track.download_problem.clone().filter(|_| !track.is_available_offline).map(|p| view! {
                <dt>"Download"</dt>
                <dd><span class="badge badge-error">{problem_label(&p)}</span>" YouTube refused the last download."</dd>
            })}
            <dt>"Added"</dt><dd>{track.created_at.get(..10).unwrap_or(&track.created_at).to_owned()}</dd>
        </dl>

        <section class="panel">
            <h2>"In playlists"</h2>
            {if detail.playlists.is_empty() {
                view! { <p class="muted">"Not in any playlist yet."</p> }.into_any()
            } else {
                view! {
                    <ul class="chips">
                        {detail.playlists.into_iter().map(|p| view! {
                            <li><A href=format!("/playlists/{}", p.id)>{p.name}</A></li>
                        }).collect_view()}
                    </ul>
                }.into_any()
            }}
        </section>
    }
}
