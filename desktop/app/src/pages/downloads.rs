use leptos::prelude::*;
use shared::LibraryChanges;
use shared::{AppError, DownloadState, Downloads as DownloadList};

use crate::components::{Art, ErrorView, Loading};
use crate::ipc;
use crate::state::{on_change, error_code_label};

#[component]
pub fn Downloads() -> impl IntoView {
    let downloads = RwSignal::new(None::<Result<DownloadList, AppError>>);

    let refresh = move || leptos::task::spawn_local(async move { downloads.set(Some(ipc::youtube_downloads().await)) });
    refresh();
    on_change(LibraryChanges::touches_downloads, refresh);
    // Poll while anything is waiting or downloading (a fallback when live updates are down).
    let handle = set_interval_with_handle(
        move || {
            let active = downloads.with_untracked(|d| {
                matches!(d, Some(Ok(list)) if list.jobs.iter().any(|j| matches!(j.state, DownloadState::Queued | DownloadState::Running)))
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
                <div>
                    <h1>"Downloads"</h1>
                    {move || downloads.with(|d| match d {
                        Some(Ok(list)) if !list.jobs.is_empty() => Some(view! { <p class="muted">{summary(list)}</p> }),
                        _ => None,
                    })}
                </div>
                <button class="ghost" on:click=move |_| refresh()>"Refresh"</button>
            </header>
            {move || downloads.with(|d| d.as_ref().and_then(|d| d.as_ref().ok()).and_then(|list| list.paused_until.clone()).map(|until| view! {
                <div class="banner" role="status">
                    <strong>"Resting until " {local_time(&until)}</strong>
                    " YouTube asked us to slow down, so downloads wait a while and then carry on by themselves."
                </div>
            }))}
            {move || match downloads.get() {
                None => view! { <Loading /> }.into_any(),
                Some(Err(error)) => view! { <ErrorView error /> }.into_any(),
                Some(Ok(list)) if list.jobs.is_empty() => view! {
                    <p class="muted">"No downloads yet. Download songs from YouTube Music, or a whole playlist from its page, to play them offline."</p>
                }.into_any(),
                Some(Ok(list)) => view! {
                    <table class="tracks">
                        <thead><tr><th>"Song"</th><th>"State"</th><th>"Details"</th></tr></thead>
                        <tbody>
                            {list.jobs.into_iter().map(|job| {
                                let (label, class) = match job.state {
                                    DownloadState::Queued => ("Queued", "badge"),
                                    DownloadState::Running => ("Downloading…", "badge badge-active"),
                                    DownloadState::Completed => ("Done", "badge badge-offline"),
                                    DownloadState::Failed => ("Failed", "badge badge-error"),
                                };
                                let details = match (&job.error_code, &job.error) {
                                    (Some(code), _) if error_code_label(code).is_some() => error_code_label(code).unwrap_or_default().to_owned(),
                                    (_, Some(error)) => error.clone(),
                                    _ if job.track_id.is_some() => "In the library, playable offline".to_owned(),
                                    _ => String::new(),
                                };
                                view! {
                                    <tr>
                                        <td class="col-title">
                                            <div class="title-cell">
                                                <Art video_id=Some(job.video_id.clone()) />
                                                {match job.title.clone() {
                                                    Some(title) => view! { <span>{title}</span> }.into_any(),
                                                    None => view! { <code>{job.video_id.clone()}</code> }.into_any(),
                                                }}
                                            </div>
                                        </td>
                                        <td><span class=class>{label}</span></td>
                                        <td class="muted" title=job.error.clone().unwrap_or_default()>{details}</td>
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

/// "3 waiting, 1 downloading, 40 done, 2 failed".
fn summary(list: &DownloadList) -> String {
    let count = |state: DownloadState| list.jobs.iter().filter(|j| j.state == state).count();
    [
        (count(DownloadState::Queued), "waiting"),
        (count(DownloadState::Running), "downloading"),
        (count(DownloadState::Completed), "done"),
        (count(DownloadState::Failed), "failed"),
    ]
    .into_iter()
    .filter(|(n, _)| *n > 0)
    .map(|(n, what)| format!("{n} {what}"))
    .collect::<Vec<_>>()
    .join(", ")
}

/// An ISO timestamp as a local clock time, e.g. "14:35".
fn local_time(iso: &str) -> String {
    let date = js_sys::Date::new(&wasm_bindgen::JsValue::from_str(iso));
    if date.get_time().is_nan() {
        return iso.to_owned();
    }
    format!("{:02}:{:02}", date.get_hours(), date.get_minutes())
}
