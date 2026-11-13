//! End-to-end check of live updates against a running old-country server: the desktop's own
//! SignalR client (src/live.rs) connected to the real hub, and a change made through the API.
//!
//! Off by default (it needs a server, and it creates and deletes a playlist there). Run with:
//!   OLDCOUNTRY_LIVE_SERVER=http://127.0.0.1:5000 cargo test -p old-country --test live_hub -- --nocapture

use std::sync::{Arc, RwLock};
use std::time::Duration;

use old_country_lib::api::ApiClient;
use old_country_lib::live::{self, Listener};
use shared::{LibraryChanges, LiveStatus};
use tokio::sync::mpsc::{UnboundedSender, unbounded_channel};

struct Forward(UnboundedSender<Result<LibraryChanges, LiveStatus>>);

impl Listener for Forward {
    fn changed(&self, changes: LibraryChanges) {
        let _ = self.0.send(Ok(changes));
    }

    fn status(&self, status: LiveStatus) {
        let _ = self.0.send(Err(status));
    }

    fn announced(&self, _news: Vec<shared::Announcement>) {}
}

#[tokio::test]
async fn a_playlist_made_through_the_api_is_heard_live() {
    let Some(base) = std::env::var("OLDCOUNTRY_LIVE_SERVER").ok().filter(|s| !s.is_empty()) else {
        eprintln!("skipped: set OLDCOUNTRY_LIVE_SERVER to run");
        return;
    };
    let url = Arc::new(RwLock::new(base));
    let (events, mut heard) = unbounded_channel();
    let client = tokio::spawn(live::run(Arc::clone(&url), Arc::new(Forward(events))));

    let connected = tokio::time::timeout(Duration::from_secs(10), async {
        while let Some(event) = heard.recv().await {
            eprintln!("heard {event:?}");
            if matches!(event, Err(LiveStatus::Live)) {
                return;
            }
        }
    })
    .await;
    assert!(connected.is_ok(), "never connected");

    let api = ApiClient::new(reqwest::Client::new(), url);
    let playlist = api.create_playlist("live hub check", None).await.expect("create a playlist");
    let id = playlist.id.clone();
    let found = tokio::time::timeout(Duration::from_secs(10), async {
        while let Some(event) = heard.recv().await {
            eprintln!("heard {event:?}");
            if let Ok(changes) = &event
                && changes.playlists.contains(&id)
            {
                return true;
            }
        }
        false
    })
    .await
    .unwrap_or(false);
    api.delete_playlist(&id).await.expect("clean up the playlist");
    client.abort();
    assert!(found, "the change never arrived");
}
