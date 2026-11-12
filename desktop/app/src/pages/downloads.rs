use leptos::prelude::*;
use shared::{AppError, DownloadState, DownloadStatus};

use crate::components::{ErrorView, Loading};
use crate::ipc;

#[component]
pub fn Downloads() -> impl IntoView {
    let jobs = RwSignal::new(None::<Result<Vec<DownloadStatus>, AppError>>);

    let refresh = move || leptos::task::spawn_local(async move { jobs.set(Some(ipc::youtube_downloads().await)) });
    refresh();
    // Poll while anything is waiting or downloading.
    let handle = set_interval_with_handle(
        move || {
            let active = jobs.with_untracked(|j| {
                matches!(j, Some(Ok(list)) if list.iter().any(|d| matches!(d.state, DownloadState::Queued | DownloadState::Running)))
            });
            if active {
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

    view! {
        <section class="page">
            <header class="page-head">
                <h1>"Downloads"</h1>
                <button class="ghost" on:click=move |_| refresh()>"Refresh"</button>
            </header>
            {move || match jobs.get() {
                None => view! { <Loading /> }.into_any(),
                Some(Err(error)) => view! { <ErrorView error /> }.into_any(),
                Some(Ok(list)) if list.is_empty() => view! {
                    <p class="muted">"No downloads yet. Download songs from YouTube Music to play them offline."</p>
                }.into_any(),
                Some(Ok(list)) => view! {
                    <table class="tracks">
                        <thead><tr><th>"Video"</th><th>"State"</th><th>"Details"</th></tr></thead>
                        <tbody>
                            {list.into_iter().map(|job| {
                                let (label, class) = match job.state {
                                    DownloadState::Queued => ("Queued", "badge"),
                                    DownloadState::Running => ("Downloading…", "badge badge-active"),
                                    DownloadState::Completed => ("Done", "badge badge-offline"),
                                    DownloadState::Failed => ("Failed", "badge badge-error"),
                                };
                                view! {
                                    <tr>
                                        <td><code>{job.video_id.clone()}</code></td>
                                        <td><span class=class>{label}</span></td>
                                        <td class="muted">{job.error.clone().unwrap_or_else(|| {
                                            if job.track_id.is_some() { "Added to the library, playable offline".into() } else { String::new() }
                                        })}</td>
                                    </tr>
                                }
                            }).collect_view()}
                        </tbody>
                    </table>
                }.into_any(),
            }}
        </section>
    }
}
