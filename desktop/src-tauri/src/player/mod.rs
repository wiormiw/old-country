//! Native playback: fetch ([`cache`]), decode ([`decoder`], [`source`]), and play ([`engine`])
//! through a [`queue`].

pub mod cache;
pub mod decoder;
pub mod engine;
pub mod queue;
pub mod source;

pub use engine::{Command, PlayerHandle, ServerUrl};

#[cfg(test)]
mod decoding_tests;
