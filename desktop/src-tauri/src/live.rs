//! Live updates: a small SignalR client for the server's `/hubs/library`.
//!
//! There's no official SignalR client for Rust, and the hub only ever sends one message
//! (`Changed`), so this speaks the protocol directly over a WebSocket rather than pulling in a
//! community crate. What it implements, and why:
//!
//! - **No negotiate call**: it connects straight to the hub's WebSocket (the server allows
//!   WebSockets without negotiation), so there is one round trip and no connection token.
//! - **Handshake**: `{"protocol":"json","version":1}`, then the server's `{}` (or an error).
//! - **Framing**: every message is JSON ending in the record separator `\x1e`; one WebSocket frame
//!   can hold several, and in principle a message could span frames, so text is buffered.
//! - **Keepalive**: the client sends a ping (`{"type":6}`) every 15 s, as the server expects;
//!   if nothing at all arrives for 30 s (the server pings every 15 s), the link is dead.
//! - **Close** (`type 7`): the server may say why and whether to reconnect.
//! - **Reconnects**: exponential backoff with jitter, forever; and right away when the server
//!   address changes in Settings. Every successful (re)connect reports "everything changed", so
//!   the UI catches up on whatever happened while it was away.

use std::sync::Arc;
use std::time::Duration;

use futures_util::{SinkExt, StreamExt};
use shared::{LibraryChanges, LiveStatus};
use tokio::time::{Instant, MissedTickBehavior, interval, sleep, timeout};
use tokio_tungstenite::connect_async;
use tokio_tungstenite::tungstenite::Message as Frame;
use tokio_tungstenite::tungstenite::client::IntoClientRequest;
use tokio_tungstenite::tungstenite::http::HeaderValue;
use tokio_tungstenite::tungstenite::http::header::USER_AGENT;

use crate::player::ServerUrl;

/// The hub's path on the server.
pub const HUB_PATH: &str = "/hubs/library";

/// How often to ping the server; the server's own keepalive is the same.
const PING_EVERY: Duration = Duration::from_secs(15);
/// Silence after which the connection counts as dead (the server's client timeout is the same).
const SERVER_TIMEOUT: Duration = Duration::from_secs(30);
/// How long connecting, and then the handshake, may each take.
const CONNECT_TIMEOUT: Duration = Duration::from_secs(10);
/// How often to look again when no server address is set yet.
const NO_SERVER_RECHECK: Duration = Duration::from_secs(2);

/// The SignalR JSON hub protocol: pure functions, no I/O, tested below.
pub mod protocol {
    use serde_json::Value;

    /// Ends every message.
    pub const RECORD_SEPARATOR: char = '\u{1e}';

    /// The first thing a client sends.
    pub fn handshake_request() -> String {
        format!(r#"{{"protocol":"json","version":1}}{RECORD_SEPARATOR}"#)
    }

    /// A keepalive ping.
    pub fn ping() -> String {
        format!(r#"{{"type":6}}{RECORD_SEPARATOR}"#)
    }

    #[derive(Debug, PartialEq)]
    pub enum Message {
        /// The server calls a client method: `{"type":1,"target":"Changed","arguments":[…]}`.
        Invocation { target: String, arguments: Vec<Value> },
        Ping,
        /// The server is closing the connection, maybe saying why.
        Close { error: Option<String>, allow_reconnect: bool },
        /// Something this client doesn't use (stream items, completions, acks…).
        Other,
    }

    /// Adds received text to `buffer` and takes out every complete record (without its separator).
    pub fn records(buffer: &mut String, text: &str) -> Vec<String> {
        buffer.push_str(text);
        let Some(last) = buffer.rfind(RECORD_SEPARATOR) else { return Vec::new() };
        let rest = buffer.split_off(last + RECORD_SEPARATOR.len_utf8());
        let complete = std::mem::replace(buffer, rest);
        complete.split(RECORD_SEPARATOR).filter(|record| !record.is_empty()).map(str::to_owned).collect()
    }

    /// The server's answer to the handshake: `{}` when it accepts.
    pub fn parse_handshake(record: &str) -> Result<(), String> {
        let value: Value = serde_json::from_str(record).map_err(|e| format!("unreadable handshake answer: {e}"))?;
        match value.get("error").and_then(Value::as_str) {
            Some(error) => Err(format!("the server refused the handshake: {error}")),
            None => Ok(()),
        }
    }

    pub fn parse(record: &str) -> Result<Message, serde_json::Error> {
        let value: Value = serde_json::from_str(record)?;
        Ok(match value.get("type").and_then(Value::as_u64) {
            Some(1) => Message::Invocation {
                target: value.get("target").and_then(Value::as_str).unwrap_or_default().to_owned(),
                arguments: value.get("arguments").and_then(Value::as_array).cloned().unwrap_or_default(),
            },
            Some(6) => Message::Ping,
            Some(7) => Message::Close {
                error: value.get("error").and_then(Value::as_str).map(str::to_owned),
                allow_reconnect: value.get("allowReconnect").and_then(Value::as_bool).unwrap_or(false),
            },
            _ => Message::Other,
        })
    }

    /// `http://host:5000` → `ws://host:5000/hubs/library` (`https` → `wss`).
    pub fn hub_url(base: &str) -> Result<String, String> {
        let base = base.trim().trim_end_matches('/');
        let rest = if let Some(rest) = base.strip_prefix("http://") {
            format!("ws://{rest}")
        } else if let Some(rest) = base.strip_prefix("https://") {
            format!("wss://{rest}")
        } else {
            return Err(format!("not an http(s) server address: {base:?}"));
        };
        Ok(format!("{rest}{}", super::HUB_PATH))
    }
}

/// What the live connection reports to the rest of the app.
pub trait Listener: Send + Sync + 'static {
    fn changed(&self, changes: LibraryChanges);
    fn status(&self, status: LiveStatus);
}

/// 1 s, 2 s, 4 s … capped at 30 s, plus up to 30 % at random so clients don't retry in step.
pub fn backoff(failures: u32, jitter: f64) -> Duration {
    let base = Duration::from_secs(1 << failures.saturating_sub(1).min(5)).min(Duration::from_secs(30));
    base.mul_f64(1.0 + 0.3 * jitter.clamp(0.0, 1.0))
}

/// How a session ended without an error.
enum Ended {
    /// The server address changed: connect to the new one now.
    ServerChanged,
    /// The server closed the connection.
    Closed,
}

/// Keeps a connection to the server's hub for as long as the app runs.
pub async fn run(server: ServerUrl, listener: Arc<dyn Listener>) {
    let mut failures = 0u32;
    loop {
        let base = server.read().expect("server url lock").clone();
        if base.is_empty() {
            listener.status(LiveStatus::Off);
            sleep(NO_SERVER_RECHECK).await;
            continue;
        }
        let url = match protocol::hub_url(&base) {
            Ok(url) => url,
            Err(error) => {
                log::warn!("live updates off: {error}");
                listener.status(LiveStatus::Off);
                sleep(NO_SERVER_RECHECK).await;
                continue;
            }
        };
        listener.status(if failures == 0 { LiveStatus::Connecting } else { LiveStatus::Reconnecting });
        match session(&url, &server, &base, listener.as_ref(), &mut failures).await {
            Ok(Ended::ServerChanged) => {
                log::info!("server address changed; reconnecting live updates");
                failures = 0;
                continue;
            }
            Ok(Ended::Closed) => log::info!("live updates: the server closed the connection"),
            Err(error) => log::debug!("live updates: {error}"),
        }
        failures += 1;
        listener.status(LiveStatus::Reconnecting);
        sleep(backoff(failures, fastrand::f64())).await;
    }
}

/// One connection, from connecting to its end. Resets `failures` once the handshake succeeds.
async fn session(url: &str, server: &ServerUrl, base: &str, listener: &dyn Listener, failures: &mut u32) -> Result<Ended, String> {
    let mut request = url.into_client_request().map_err(|e| format!("bad hub address {url}: {e}"))?;
    request.headers_mut().insert(USER_AGENT, HeaderValue::from_static(concat!("old-country-desktop/", env!("CARGO_PKG_VERSION"))));
    let (socket, _) = timeout(CONNECT_TIMEOUT, connect_async(request))
        .await
        .map_err(|_| format!("connecting to {url} timed out"))?
        .map_err(|e| format!("couldn't connect to {url}: {e}"))?;
    let (mut write, mut read) = socket.split();
    write.send(Frame::text(protocol::handshake_request())).await.map_err(|e| e.to_string())?;

    // The first record is the handshake answer; anything after it in the same frame is kept.
    let mut buffer = String::new();
    let mut pending = timeout(CONNECT_TIMEOUT, async {
        loop {
            match read.next().await {
                Some(Ok(Frame::Text(text))) => {
                    let records = protocol::records(&mut buffer, &text);
                    if !records.is_empty() {
                        return Ok(records);
                    }
                }
                Some(Ok(_)) => {}
                Some(Err(e)) => return Err(e.to_string()),
                None => return Err("closed during the handshake".to_owned()),
            }
        }
    })
    .await
    .map_err(|_| "the handshake timed out".to_owned())??;
    protocol::parse_handshake(&pending.remove(0))?;

    log::info!("live updates connected to {url}");
    *failures = 0;
    listener.status(LiveStatus::Live);
    listener.changed(LibraryChanges::everything()); // catch up on anything missed while away

    let mut heard = Instant::now();
    let mut ping = interval(PING_EVERY);
    ping.set_missed_tick_behavior(MissedTickBehavior::Delay);
    ping.tick().await; // the first tick is immediate; the handshake just spoke

    let handle = |records: Vec<String>| -> Option<Ended> {
        for record in records {
            match protocol::parse(&record) {
                Ok(protocol::Message::Invocation { target, arguments }) if target == "Changed" => {
                    match arguments.into_iter().next().map(serde_json::from_value::<LibraryChanges>) {
                        Some(Ok(changes)) => listener.changed(changes),
                        other => log::warn!("unreadable Changed message: {other:?}"),
                    }
                }
                Ok(protocol::Message::Close { error, allow_reconnect }) => {
                    log::info!("the server closed live updates ({}; reconnect: {allow_reconnect})", error.as_deref().unwrap_or("no reason given"));
                    return Some(Ended::Closed);
                }
                Ok(_) => {}
                Err(e) => log::warn!("unreadable hub message: {e}"),
            }
        }
        None
    };
    if let Some(ended) = handle(pending) {
        return Ok(ended);
    }

    loop {
        tokio::select! {
            frame = read.next() => match frame {
                Some(Ok(Frame::Text(text))) => {
                    heard = Instant::now();
                    if let Some(ended) = handle(protocol::records(&mut buffer, &text)) {
                        return Ok(ended);
                    }
                }
                Some(Ok(Frame::Close(_))) | None => return Ok(Ended::Closed),
                Some(Ok(_)) => heard = Instant::now(), // WebSocket-level ping/pong also proves the link
                Some(Err(e)) => return Err(e.to_string()),
            },
            _ = ping.tick() => {
                if heard.elapsed() > SERVER_TIMEOUT {
                    return Err(format!("nothing from the server for {} s", SERVER_TIMEOUT.as_secs()));
                }
                if *server.read().expect("server url lock") != base {
                    let _ = write.send(Frame::Close(None)).await;
                    return Ok(Ended::ServerChanged);
                }
                write.send(Frame::text(protocol::ping())).await.map_err(|e| e.to_string())?;
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::protocol::{self, Message};
    use super::*;

    #[test]
    fn records_are_split_on_the_separator_and_partial_ones_wait() {
        let mut buffer = String::new();
        assert_eq!(protocol::records(&mut buffer, "{}\u{1e}{\"type\":6}\u{1e}{\"ty"), vec!["{}", r#"{"type":6}"#]);
        assert_eq!(buffer, r#"{"ty"#);
        assert!(protocol::records(&mut buffer, "pe\":6").is_empty());
        assert_eq!(protocol::records(&mut buffer, "}\u{1e}"), vec![r#"{"type":6}"#]);
        assert!(buffer.is_empty());
    }

    #[test]
    fn messages_parse_by_type() {
        let changed = protocol::parse(r#"{"type":1,"target":"Changed","arguments":[{"playlists":["p1"],"tracks":true}]}"#).unwrap();
        let Message::Invocation { target, arguments } = changed else { panic!("not an invocation") };
        assert_eq!(target, "Changed");
        let changes: LibraryChanges = serde_json::from_value(arguments[0].clone()).unwrap();
        assert_eq!(changes, LibraryChanges { playlists: vec!["p1".into()], tracks: true, ..LibraryChanges::default() });

        assert_eq!(protocol::parse(r#"{"type":6}"#).unwrap(), Message::Ping);
        assert_eq!(
            protocol::parse(r#"{"type":7,"error":"shutting down","allowReconnect":true}"#).unwrap(),
            Message::Close { error: Some("shutting down".into()), allow_reconnect: true }
        );
        assert_eq!(protocol::parse(r#"{"type":3,"invocationId":"1"}"#).unwrap(), Message::Other);
    }

    #[test]
    fn handshake_answers_are_checked() {
        assert!(protocol::parse_handshake("{}").is_ok());
        assert!(protocol::parse_handshake(r#"{"error":"Requested protocol 'xml' is not available."}"#).is_err());
    }

    #[test]
    fn hub_urls_follow_the_server_address() {
        assert_eq!(protocol::hub_url("http://192.168.1.23:5000/").unwrap(), "ws://192.168.1.23:5000/hubs/library");
        assert_eq!(protocol::hub_url("https://music.home").unwrap(), "wss://music.home/hubs/library");
        assert!(protocol::hub_url("music.home").is_err());
    }

    #[test]
    fn retries_back_off_to_thirty_seconds_with_jitter() {
        assert_eq!(backoff(1, 0.0), Duration::from_secs(1));
        assert_eq!(backoff(3, 0.0), Duration::from_secs(4));
        assert_eq!(backoff(6, 0.0), Duration::from_secs(30));
        assert_eq!(backoff(50, 0.0), Duration::from_secs(30));
        assert_eq!(backoff(50, 1.0), Duration::from_secs(39));
    }

    /// Records what the client reports.
    struct Recorder(tokio::sync::mpsc::UnboundedSender<String>);

    impl Listener for Recorder {
        fn changed(&self, changes: LibraryChanges) {
            let _ = self.0.send(format!("changed {}", serde_json::to_string(&changes).unwrap()));
        }

        fn status(&self, status: LiveStatus) {
            let _ = self.0.send(format!("status {status:?}"));
        }
    }

    /// A stand-in hub: answers the handshake, sends one `Changed`, then closes the first
    /// connection (allowing a reconnect) and keeps the second one open.
    async fn fake_hub() -> (std::net::SocketAddr, tokio::sync::mpsc::UnboundedReceiver<String>) {
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let address = listener.local_addr().unwrap();
        let (handshakes, received) = tokio::sync::mpsc::unbounded_channel();
        tokio::spawn(async move {
            for connection in 1.. {
                let (stream, _) = listener.accept().await.unwrap();
                let mut socket = tokio_tungstenite::accept_async(stream).await.unwrap();
                let Some(Ok(Frame::Text(handshake))) = socket.next().await else { panic!("no handshake") };
                let _ = handshakes.send(handshake.to_string());
                let change = if connection == 1 { r#"{"playlists":["p1"]}"# } else { r#"{"tracks":true}"# };
                // The handshake answer and a message in one frame, as servers may send them.
                let frame = format!("{{}}\u{1e}{{\"type\":1,\"target\":\"Changed\",\"arguments\":[{change}]}}\u{1e}");
                socket.send(Frame::text(frame)).await.unwrap();
                if connection == 1 {
                    socket.send(Frame::text("{\"type\":7,\"error\":\"restarting\",\"allowReconnect\":true}\u{1e}")).await.unwrap();
                } else {
                    tokio::spawn(async move { while socket.next().await.is_some() {} });
                }
            }
        });
        (address, received)
    }

    #[tokio::test]
    async fn the_client_handshakes_hears_changes_and_comes_back_after_the_server_closes() {
        let (address, mut handshakes) = fake_hub().await;
        let server: ServerUrl = Arc::new(std::sync::RwLock::new(format!("http://{address}")));
        let (events, mut heard) = tokio::sync::mpsc::unbounded_channel();
        let client = tokio::spawn(run(server, Arc::new(Recorder(events))));

        let mut log = Vec::new();
        let finished = timeout(Duration::from_secs(10), async {
            while let Some(event) = heard.recv().await {
                log.push(event);
                if log.iter().any(|e| e.contains(r#""tracks":true"#)) {
                    break;
                }
            }
        })
        .await;
        client.abort();
        assert!(finished.is_ok(), "never heard the second change: {log:#?}");

        assert_eq!(handshakes.recv().await.unwrap(), protocol::handshake_request());
        assert_eq!(handshakes.recv().await.unwrap(), protocol::handshake_request(), "it reconnected");
        let position = |needle: &str| log.iter().position(|e| e.contains(needle)).unwrap_or_else(|| panic!("no {needle}: {log:#?}"));
        assert!(position("status Live") < position(r#""playlists":["p1"]"#));
        assert!(position(r#""playlists":["p1"]"#) < position("status Reconnecting"));
        assert!(position("status Reconnecting") < position(r#""tracks":true"#));
        assert_eq!(log.iter().filter(|e| e.contains(r#""everything":true"#)).count(), 2, "catches up after each connect");
    }
}
