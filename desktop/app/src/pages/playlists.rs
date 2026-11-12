use std::time::Duration;

use leptos::prelude::*;
use leptos_router::components::A;
use leptos_router::hooks::{use_navigate, use_params_map};
use shared::timespan::display;
use shared::{AppError, Playlist, PlaylistDownloadReport, PlaylistEntry};

use crate::components::{ErrorView, Loading};
use crate::ipc;
use crate::state::{entry_item, import_message, is_lasting_problem, problem_label, time_ago, use_notice};

#[component]
pub fn Playlists() -> impl IntoView {
    let playlists = LocalResource::new(ipc::list_playlists);
    let name = RwSignal::new(String::new());
    let error = RwSignal::new(None::<AppError>);
    let creating = RwSignal::new(false);

    let create = move |ev: leptos::ev::SubmitEvent| {
        ev.prevent_default();
        let value = name.get_untracked();
        creating.set(true);
        leptos::task::spawn_local(async move {
            match ipc::create_playlist(value, None).await {
                Ok(_) => {
                    name.set(String::new());
                    error.set(None);
                    playlists.refetch();
                }
                Err(e) => error.set(Some(e)),
            }
            creating.set(false);
        });
    };

    view! {
        <section class="page">
            <header class="page-head"><h1>"Playlists"</h1></header>
            <form class="inline-form" on:submit=create>
                <input type="text" placeholder="New playlist name" bind:value=name aria-label="New playlist name" />
                <button class="primary" type="submit" aria-busy=move || creating.get().to_string()
                    prop:disabled=move || creating.get()>"Create"</button>
            </form>
            {move || error.get().map(|e| view! { <p class="field-error">{e.message}</p> })}
            {move || match playlists.get() {
                None => view! { <Loading /> }.into_any(),
                Some(Err(error)) => view! { <ErrorView error /> }.into_any(),
                Some(Ok(list)) if list.is_empty() => view! { <p class="muted">"No playlists yet."</p> }.into_any(),
                Some(Ok(list)) => view! {
                    <ul class="cards appear">
                        {list.into_iter().map(|p| view! {
                            <li>
                                <A href=format!("/playlists/{}", p.id)>
                                    <span class="card-title">{p.name.clone()}</span>
                                    <span class="muted">
                                        {format!("{} tracks", p.track_count)}
                                        {p.you_tube_list_id.is_some().then(|| view! { <span class="badge badge-youtube">"YouTube"</span> })}
                                    </span>
                                    {p.description.clone().map(|d| view! { <span class="card-desc">{d}</span> })}
                                </A>
                            </li>
                        }).collect_view()}
                    </ul>
                }.into_any(),
            }}
        </section>
    }
}

#[component]
pub fn PlaylistDetail() -> impl IntoView {
    let params = use_params_map();
    let id = Memo::new(move |_| params.with(|p| p.get("id").unwrap_or_default()));
    let playlist = LocalResource::new(move || ipc::get_playlist(id.get()));

    view! {
        <section class="page">
            {move || match playlist.get() {
                None => view! { <Loading /> }.into_any(),
                Some(Err(error)) => view! { <ErrorView error /> }.into_any(),
                Some(Ok(p)) => view! { <PlaylistView playlist=p reload=move || playlist.refetch() /> }.into_any(),
            }}
        </section>
    }
}

/// How long a moved or removed row animates; matches app.css.
const ROW_ANIMATION: Duration = Duration::from_millis(220);

#[component]
fn PlaylistView(playlist: Playlist, reload: impl Fn() + Copy + Send + Sync + 'static) -> impl IntoView {
    let notice = use_notice();
    let navigate = use_navigate();
    let editing = RwSignal::new(false);
    let saving = RwSignal::new(false);
    let confirm_delete = RwSignal::new(false);
    let deleting = RwSignal::new(false);
    let name = RwSignal::new(playlist.name.clone());
    let description = RwSignal::new(playlist.description.clone().unwrap_or_default());
    let id = StoredValue::new(playlist.id.clone());

    // The rows as shown. Moves and removals change them here first, so they animate at once;
    // the server follows, and if it refuses, the playlist reloads with the real order.
    let entries = RwSignal::new(playlist.entries.clone());
    let empty = Memo::new(move |_| entries.with(Vec::is_empty));
    // One move or removal at a time, so positions sent to the server always match the rows.
    let changing = RwSignal::new(false);
    // The two rows a move just swapped: (where the moved row is now, where its neighbour is now).
    let swapped = RwSignal::new(None::<(usize, usize)>);
    let leaving = RwSignal::new(None::<usize>);
    // Rows fade in one after another when the playlist opens, not again after each change.
    let opening = RwSignal::new(true);
    set_timeout(move || opening.set(false), Duration::from_millis(700));

    // Downloading: what's left to get, what YouTube refused before, and live progress.
    let to_get = Memo::new(move |_| distinct_songs(&entries.get(), needs_download));
    let skipped = Memo::new(move |_| distinct_songs(&entries.get(), |e| {
        !e.is_available_offline && e.download_problem.as_deref().is_some_and(is_lasting_problem)
    }));
    let downloaded = Memo::new(move |_| distinct_songs(&entries.get(), |e| e.is_available_offline));
    let songs = Memo::new(move |_| distinct_songs(&entries.get(), |_| true));
    let queueing = RwSignal::new(false);
    let syncing = RwSignal::new(false);
    let sync_now = move || {
        syncing.set(true);
        leptos::task::spawn_local(async move {
            match ipc::sync_playlist(id.get_value()).await {
                Ok(report) => {
                    notice.info(import_message(&report));
                    reload();
                }
                Err(e) => notice.error(e.message),
            }
            syncing.set(false);
        });
    };
    let watching = RwSignal::new(false);
    let only_missing = RwSignal::new(false);

    let download_all = move |retry_skipped: bool| {
        queueing.set(true);
        leptos::task::spawn_local(async move {
            match ipc::download_playlist(id.get_value(), retry_skipped).await {
                Ok(report) => {
                    notice.info(download_message(&report));
                    watching.set(report.queued + report.already_queued > 0);
                }
                Err(e) => notice.error(e.message),
            }
            queueing.set(false);
        });
    };

    // While downloads run, refresh which songs are offline (and which YouTube refused) every few
    // seconds; stop once nothing has changed for a while.
    let unchanged = StoredValue::new(0u32);
    let poll = set_interval_with_handle(
        move || {
            if !watching.get_untracked() || changing.get_untracked() {
                return;
            }
            leptos::task::spawn_local(async move {
                let Ok(fresh) = ipc::get_playlist(id.get_value()).await else { return };
                let changed = entries.try_update(|list| merge_download_flags(list, &fresh.entries)).unwrap_or(false);
                unchanged.update_value(|n| *n = if changed { 0 } else { *n + 1 });
                if to_get.get_untracked() == 0 || unchanged.get_value() >= WATCH_IDLE_POLLS {
                    watching.set(false);
                    unchanged.set_value(0);
                }
            });
        },
        Duration::from_secs(4),
    )
    .ok();
    on_cleanup(move || {
        if let Some(poll) = poll {
            poll.clear();
        }
    });

    let filter = RwSignal::new(String::new());
    let filtering = move || !filter.with(|f| f.trim().is_empty()) || only_missing.get();
    let matches = move |title: &str, artist: &str| {
        filter.with(|f| {
            let f = f.trim().to_lowercase();
            f.is_empty() || title.to_lowercase().contains(&f) || artist.to_lowercase().contains(&f)
        })
    };

    let after_server = move |result: Result<(), AppError>| {
        if let Err(e) = result {
            notice.error(e.message);
            reload();
        }
        changing.set(false);
    };

    let move_entry = move |from: usize, to: usize| {
        if changing.get_untracked() || to >= entries.with_untracked(Vec::len) {
            return;
        }
        changing.set(true);
        entries.update(|list| list.swap(from, to));
        swapped.set(Some((to, from)));
        set_timeout(move || swapped.set(None), ROW_ANIMATION);
        leptos::task::spawn_local(async move {
            after_server(ipc::move_playlist_entry(id.get_value(), from as u32, to as u32).await);
        });
    };

    let remove_entry = move |index: usize| {
        if changing.get_untracked() {
            return;
        }
        changing.set(true);
        leaving.set(Some(index));
        set_timeout(
            move || {
                entries.update(|list| {
                    list.remove(index);
                });
                leaving.set(None);
                leptos::task::spawn_local(async move {
                    after_server(ipc::remove_playlist_entry(id.get_value(), index as u32).await);
                });
            },
            ROW_ANIMATION,
        );
    };

    // Which way a row slides in after a swap, if it was one of the two.
    let slide = move |index: usize| {
        swapped.with(|s| match *s {
            Some((now, was)) if index == now => Some(if was > now { "from-below" } else { "from-above" }),
            Some((moved, now)) if index == now => Some(if moved < now { "from-above" } else { "from-below" }),
            _ => None,
        })
    };

    let save = move |ev: leptos::ev::SubmitEvent| {
        ev.prevent_default();
        let (n, d) = (name.get_untracked(), description.get_untracked());
        saving.set(true);
        leptos::task::spawn_local(async move {
            match ipc::update_playlist(id.get_value(), n, (!d.trim().is_empty()).then_some(d)).await {
                Ok(_) => {
                    editing.set(false);
                    reload();
                }
                Err(e) => notice.error(e.message),
            }
            saving.set(false);
        });
    };

    let delete = move |_| {
        let navigate = navigate.clone();
        deleting.set(true);
        leptos::task::spawn_local(async move {
            match ipc::delete_playlist(id.get_value()).await {
                Ok(()) => {
                    notice.info("Playlist deleted.");
                    navigate("/playlists", Default::default());
                }
                Err(e) => {
                    deleting.set(false);
                    notice.error(e.message);
                }
            }
        });
    };

    let queue = move || entries.with_untracked(|list| list.iter().map(entry_item).collect::<Vec<_>>());

    view! {
        <header class="page-head">
            <div>
                <h1>{playlist.name.clone()}</h1>
                <p class="muted">{move || entries.with(|list| {
                    format!("{} tracks · {}", list.len(), display(list.iter().map(|e| e.duration).sum::<f64>()))
                })}
                    {move || (songs.get() > 0).then(|| view! {
                        <span class:downloading=move || watching.get()>
                            {format!(" · {} of {} downloaded", downloaded.get(), songs.get())}
                        </span>
                    })}
                </p>
                {playlist.description.clone().map(|d| view! { <p>{d}</p> })}
                {playlist.you_tube_list_id.is_some().then(|| view! {
                    <p class="sync-line">
                        <span class="badge badge-youtube">"YouTube Music"</span>
                        {match playlist.you_tube_synced_at.as_deref() {
                            Some(at) => format!(" Synced {}. New songs on YouTube are added on their own.", time_ago(at)),
                            None => " Not synced yet. New songs on YouTube are added on their own.".to_owned(),
                        }}
                    </p>
                })}
            </div>
            <div class="actions">
                <button class="primary" prop:disabled=move || empty.get()
                    on:click=move |_| leptos::task::spawn_local(ipc::play(queue(), 0))>"▶ Play all"</button>
                <button class="ghost" aria-busy=move || queueing.get().to_string()
                    prop:disabled=move || { queueing.get() || to_get.get() == 0 }
                    title="Download the songs that aren't on this computer's server yet, one after another"
                    on:click=move |_| download_all(false)>
                    {move || match to_get.get() {
                        0 if skipped.get() == 0 => "✓ All downloaded".to_owned(),
                        0 => "⤓ Nothing more to get".to_owned(),
                        n => format!("⤓ Download all ({n})"),
                    }}
                </button>
                {move || (skipped.get() > 0).then(|| view! {
                    <button class="ghost" prop:disabled=move || queueing.get()
                        title="Try again the songs YouTube refused before (Premium only, private...), e.g. after getting Premium"
                        on:click=move |_| download_all(true)>{format!("↻ Retry skipped ({})", skipped.get())}</button>
                })}
                {playlist.you_tube_list_id.is_some().then(|| view! {
                    <button class="ghost" aria-busy=move || syncing.get().to_string() prop:disabled=move || syncing.get()
                        title="Add the songs that were added to this playlist on YouTube Music"
                        on:click=move |_| sync_now()>"↻ Sync"</button>
                })}
                <button class="ghost" class:active=move || editing.get() on:click=move |_| editing.update(|e| *e = !*e)>"Edit"</button>
                {move || if confirm_delete.get() {
                    view! {
                        <button class="danger" aria-busy=move || deleting.get().to_string()
                            prop:disabled=move || deleting.get() on:click=delete.clone()>"Confirm delete"</button>
                        <button class="ghost" on:click=move |_| confirm_delete.set(false)>"Keep"</button>
                    }.into_any()
                } else {
                    view! { <button class="ghost" on:click=move |_| confirm_delete.set(true)>"Delete"</button> }.into_any()
                }}
            </div>
        </header>

        <Show when=move || editing.get()>
            <form class="stack-form reveal" on:submit=save>
                <label>"Name"<input type="text" bind:value=name /></label>
                <label>"Description"<input type="text" bind:value=description /></label>
                <div>
                    <button class="primary" type="submit" aria-busy=move || saving.get().to_string()
                        prop:disabled=move || saving.get()>"Save"</button>
                </div>
            </form>
        </Show>

        <Show when=move || !empty.get() fallback=|| view! {
            <p class="muted">"This playlist is empty. Add tracks from the Library."</p>
        }>
            <div class="list-tools">
                <input type="search" class="search" placeholder="Find in this playlist" aria-label="Find in this playlist" bind:value=filter />
                <button class="ghost small" class:active=move || only_missing.get() aria-pressed=move || only_missing.get().to_string()
                    on:click=move |_| only_missing.update(|m| *m = !*m)>"Not downloaded"</button>
            </div>
            <table class="tracks" class:appear=move || opening.get()>
                <thead><tr><th class="col-num">"#"</th><th>"Title"</th><th class="col-artist">"Artist"</th><th class="col-time">"Time"</th><th class="col-actions"></th></tr></thead>
                <tbody>
                    {move || {
                        let count = entries.with(Vec::len);
                        entries.get().into_iter().enumerate().map(|(i, entry)| {
                            let (title, artist) = (entry.title.clone(), entry.artist.clone());
                            let offline = entry.is_available_offline;
                            let problem = entry.download_problem.clone().filter(|_| !offline);
                            view! {
                                <tr class:unavailable=!entry.is_available_offline && entry.you_tube_video_id.is_none()
                                    class=move || slide(i).unwrap_or_default()
                                    class:leaving=move || leaving.get() == Some(i)
                                    style:display=move || if matches(&title, &artist) && !(only_missing.get() && offline) { "" } else { "none" }>
                                    <td class="col-num">
                                        <button class="icon row-play" aria-label="Play"
                                            on:click=move |_| leptos::task::spawn_local(ipc::play(queue(), i))>"▶"</button>
                                    </td>
                                    <td class="col-title">
                                        <A href=format!("/tracks/{}", entry.track_id) attr:class="title-link">{entry.title.clone()}</A>
                                        {problem.map(|p| view! {
                                            <span class="badge badge-error problem" title="YouTube refused the last download">{problem_label(&p)}</span>
                                        })}
                                        <span class="sub-artist">{entry.artist.clone()}</span>
                                    </td>
                                    <td class="muted col-artist">{entry.artist.clone()}</td>
                                    <td class="col-time">{display(entry.duration)}</td>
                                    <td class="col-actions">
                                        // Moving only makes sense in the whole list, not a filtered one.
                                        <button class="icon" aria-label="Move up" prop:disabled=move || { i == 0 || filtering() }
                                            on:click=move |_| move_entry(i, i - 1)>"↑"</button>
                                        <button class="icon" aria-label="Move down" prop:disabled=move || { i + 1 == count || filtering() }
                                            on:click=move |_| move_entry(i, i + 1)>"↓"</button>
                                        <button class="icon" aria-label="Remove" on:click=move |_| remove_entry(i)>"✕"</button>
                                    </td>
                                </tr>
                            }
                        }).collect_view()
                    }}
                </tbody>
            </table>
        </Show>
    }
}

/// Polls with no change before the playlist stops watching its downloads.
const WATCH_IDLE_POLLS: u32 = 8;

/// A song worth queueing: on YouTube, not downloaded, and not refused for a lasting reason.
fn needs_download(entry: &PlaylistEntry) -> bool {
    !entry.is_available_offline
        && entry.you_tube_video_id.is_some()
        && !entry.download_problem.as_deref().is_some_and(is_lasting_problem)
}

/// How many different songs match (a song can be in a playlist more than once).
fn distinct_songs(entries: &[PlaylistEntry], keep: impl Fn(&PlaylistEntry) -> bool) -> usize {
    entries.iter().filter(|e| keep(e)).map(|e| e.track_id.as_str()).collect::<std::collections::HashSet<_>>().len()
}

/// Copies the server's offline flags and download problems onto the rows shown, by track, leaving
/// their order alone (the user may be reordering). `true` when anything changed.
fn merge_download_flags(shown: &mut [PlaylistEntry], fresh: &[PlaylistEntry]) -> bool {
    let latest: std::collections::HashMap<_, _> =
        fresh.iter().map(|e| (e.track_id.as_str(), (e.is_available_offline, e.download_problem.clone()))).collect();
    let mut changed = false;
    for entry in shown.iter_mut() {
        if let Some((offline, problem)) = latest.get(entry.track_id.as_str())
            && (entry.is_available_offline != *offline || entry.download_problem != *problem)
        {
            entry.is_available_offline = *offline;
            entry.download_problem = problem.clone();
            changed = true;
        }
    }
    changed
}

fn download_message(report: &PlaylistDownloadReport) -> String {
    let mut parts = vec![match report.queued {
        0 => "Nothing new to download".to_owned(),
        1 => "1 song queued for download".to_owned(),
        n => format!("{n} songs queued for download"),
    }];
    if report.already_queued > 0 {
        parts.push(format!("{} already on the way", report.already_queued));
    }
    if report.skipped > 0 {
        parts.push(format!("{} skipped (refused by YouTube before; use Retry skipped)", report.skipped));
    }
    format!("{}. See Downloads for progress.", parts.join(", "))
}
