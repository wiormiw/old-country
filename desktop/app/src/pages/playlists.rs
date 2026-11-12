use leptos::prelude::*;
use leptos_router::components::A;
use leptos_router::hooks::{use_navigate, use_params_map};
use shared::timespan::display;
use shared::{AppError, Playlist};

use crate::components::{ErrorView, Loading};
use crate::ipc;
use crate::state::{entry_item, use_notice};

#[component]
pub fn Playlists() -> impl IntoView {
    let playlists = LocalResource::new(ipc::list_playlists);
    let name = RwSignal::new(String::new());
    let error = RwSignal::new(None::<AppError>);

    let create = move |ev: leptos::ev::SubmitEvent| {
        ev.prevent_default();
        let value = name.get_untracked();
        leptos::task::spawn_local(async move {
            match ipc::create_playlist(value, None).await {
                Ok(_) => {
                    name.set(String::new());
                    error.set(None);
                    playlists.refetch();
                }
                Err(e) => error.set(Some(e)),
            }
        });
    };

    view! {
        <section class="page">
            <header class="page-head"><h1>"Playlists"</h1></header>
            <form class="inline-form" on:submit=create>
                <input type="text" placeholder="New playlist name" bind:value=name aria-label="New playlist name" />
                <button class="primary" type="submit">"Create"</button>
            </form>
            {move || error.get().map(|e| view! { <p class="field-error">{e.message}</p> })}
            {move || match playlists.get() {
                None => view! { <Loading /> }.into_any(),
                Some(Err(error)) => view! { <ErrorView error /> }.into_any(),
                Some(Ok(list)) if list.is_empty() => view! { <p class="muted">"No playlists yet."</p> }.into_any(),
                Some(Ok(list)) => view! {
                    <ul class="cards">
                        {list.into_iter().map(|p| view! {
                            <li>
                                <A href=format!("/playlists/{}", p.id)>
                                    <span class="card-title">{p.name.clone()}</span>
                                    <span class="muted">{format!("{} tracks", p.track_count)}</span>
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

#[component]
fn PlaylistView(playlist: Playlist, reload: impl Fn() + Copy + Send + Sync + 'static) -> impl IntoView {
    let notice = use_notice();
    let navigate = use_navigate();
    let editing = RwSignal::new(false);
    let confirm_delete = RwSignal::new(false);
    let name = RwSignal::new(playlist.name.clone());
    let description = RwSignal::new(playlist.description.clone().unwrap_or_default());
    let id = playlist.id.clone();
    let queue: Vec<_> = playlist.entries.iter().map(entry_item).collect();
    let count = playlist.entries.len();

    // Runs a playlist change, then reloads (positions shift after a move or removal).
    let change = move |work: std::pin::Pin<Box<dyn std::future::Future<Output = Result<(), AppError>>>>| {
        leptos::task::spawn_local(async move {
            match work.await {
                Ok(()) => reload(),
                Err(e) => notice.error(e.message),
            }
        });
    };

    let save = {
        let id = id.clone();
        move |ev: leptos::ev::SubmitEvent| {
            ev.prevent_default();
            let (id, n, d) = (id.clone(), name.get_untracked(), description.get_untracked());
            change(Box::pin(async move {
                ipc::update_playlist(id, n, (!d.trim().is_empty()).then_some(d)).await.map(drop)
            }));
            editing.set(false);
        }
    };

    let delete = {
        let id = id.clone();
        move |_| {
            let id = id.clone();
            let navigate = navigate.clone();
            leptos::task::spawn_local(async move {
                match ipc::delete_playlist(id).await {
                    Ok(()) => {
                        notice.info("Playlist deleted.");
                        navigate("/playlists", Default::default());
                    }
                    Err(e) => notice.error(e.message),
                }
            });
        }
    };

    let play_all = {
        let queue = queue.clone();
        move |_| leptos::task::spawn_local(ipc::play(queue.clone(), 0))
    };

    view! {
        <header class="page-head">
            <div>
                <h1>{playlist.name.clone()}</h1>
                <p class="muted">{format!("{count} tracks · {}", display(playlist.total_duration))}</p>
                {playlist.description.clone().map(|d| view! { <p>{d}</p> })}
            </div>
            <div class="actions">
                <button class="primary" prop:disabled=count == 0 on:click=play_all>"▶ Play all"</button>
                <button class="ghost" on:click=move |_| editing.update(|e| *e = !*e)>"Edit"</button>
                {move || if confirm_delete.get() {
                    view! {
                        <button class="danger" on:click=delete.clone()>"Confirm delete"</button>
                        <button class="ghost" on:click=move |_| confirm_delete.set(false)>"Keep"</button>
                    }.into_any()
                } else {
                    view! { <button class="ghost" on:click=move |_| confirm_delete.set(true)>"Delete"</button> }.into_any()
                }}
            </div>
        </header>

        <Show when=move || editing.get()>
            <form class="stack-form" on:submit=save.clone()>
                <label>"Name"<input type="text" bind:value=name /></label>
                <label>"Description"<input type="text" bind:value=description /></label>
                <div><button class="primary" type="submit">"Save"</button></div>
            </form>
        </Show>

        {if count == 0 {
            view! { <p class="muted">"This playlist is empty. Add tracks from the Library."</p> }.into_any()
        } else {
            view! {
                <table class="tracks">
                    <thead><tr><th class="col-num">"#"</th><th>"Title"</th><th>"Artist"</th><th class="col-time">"Time"</th><th class="col-actions"></th></tr></thead>
                    <tbody>
                        {playlist.entries.into_iter().enumerate().map(|(i, entry)| {
                            let position = entry.position;
                            let queue = queue.clone();
                            let (up, down, remove) = (id.clone(), id.clone(), id.clone());
                            view! {
                                <tr class:unavailable=!entry.is_available_offline && entry.you_tube_video_id.is_none()>
                                    <td class="col-num">
                                        <button class="icon row-play" aria-label="Play"
                                            on:click=move |_| leptos::task::spawn_local(ipc::play(queue.clone(), i))>"▶"</button>
                                    </td>
                                    <td>{entry.title.clone()}</td>
                                    <td class="muted">{entry.artist.clone()}</td>
                                    <td class="col-time">{display(entry.duration)}</td>
                                    <td class="col-actions">
                                        <button class="icon" aria-label="Move up" prop:disabled=i == 0
                                            on:click=move |_| change(Box::pin(ipc::move_playlist_entry(up.clone(), position, position - 1)))>"↑"</button>
                                        <button class="icon" aria-label="Move down" prop:disabled=i + 1 == count
                                            on:click=move |_| change(Box::pin(ipc::move_playlist_entry(down.clone(), position, position + 1)))>"↓"</button>
                                        <button class="icon" aria-label="Remove"
                                            on:click=move |_| change(Box::pin(ipc::remove_playlist_entry(remove.clone(), position)))>"✕"</button>
                                    </td>
                                </tr>
                            }
                        }).collect_view()}
                    </tbody>
                </table>
            }.into_any()
        }}
    }
}
