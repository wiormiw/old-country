//! Types shared by the desktop core (native) and the UI (wasm).
//!
//! - [`api`]: the old-country server's JSON, field for field. The server serialises camelCase,
//!   enums as strings, .NET `TimeSpan` as `"hh:mm:ss"` text and omits null fields.
//! - [`ipc`]: what the UI and the core exchange over Tauri commands and events.

pub mod api;
pub mod ipc;
pub mod timespan;

pub use api::*;
pub use ipc::*;
