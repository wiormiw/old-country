//! The play queue: its items, the order they play in, shuffle and repeat. Pure logic, so its
//! rules are tested without audio.
//!
//! Shuffle is a *bag*, not a random pick per track: the play order is a shuffled permutation, and
//! a cursor walks it. Everything before the cursor has been heard this cycle; everything after it
//! hasn't. "Next" only ever takes from the unheard part, so with repeat on, every song plays once
//! before any song plays twice. When the cycle is used up, repeat-all reshuffles for the next one
//! (never starting with the song that just ended). Songs added mid-cycle join the unheard part.

use shared::{QueueItem, RepeatMode};

#[derive(Debug)]
pub struct Queue {
    items: Vec<QueueItem>,
    /// Play order: indices into `items`, each exactly once.
    order: Vec<usize>,
    /// Position in `order` of the current item.
    cursor: Option<usize>,
    shuffle: bool,
    repeat: RepeatMode,
    rng: fastrand::Rng,
}

impl Default for Queue {
    fn default() -> Self {
        Self::with_rng(fastrand::Rng::new())
    }
}

impl Queue {
    pub fn with_rng(rng: fastrand::Rng) -> Self {
        Self { items: Vec::new(), order: Vec::new(), cursor: None, shuffle: false, repeat: RepeatMode::Off, rng }
    }

    pub fn items(&self) -> &[QueueItem] {
        &self.items
    }

    /// Indices into [`items`](Self::items) in the order they play.
    pub fn order(&self) -> &[usize] {
        &self.order
    }

    /// Index into [`items`](Self::items) of the current item.
    pub fn index(&self) -> Option<usize> {
        self.cursor.map(|c| self.order[c])
    }

    pub fn current(&self) -> Option<&QueueItem> {
        self.index().map(|i| &self.items[i])
    }

    pub fn shuffle(&self) -> bool {
        self.shuffle
    }

    pub fn repeat(&self) -> RepeatMode {
        self.repeat
    }

    pub fn set_repeat(&mut self, repeat: RepeatMode) {
        self.repeat = repeat;
    }

    /// Turning shuffle on starts a fresh bag with the current song first; turning it off goes back
    /// to list order from the current song.
    pub fn set_shuffle(&mut self, on: bool) {
        if on == self.shuffle {
            return;
        }
        self.shuffle = on;
        let current = self.index();
        if on {
            self.order = self.bag(current);
            self.cursor = current.map(|_| 0);
        } else {
            self.order = (0..self.items.len()).collect();
            self.cursor = current;
        }
    }

    /// Replaces the queue and starts at `start` (clamped to the list).
    pub fn replace(&mut self, items: Vec<QueueItem>, start: usize) {
        self.items = items;
        if self.items.is_empty() {
            self.order.clear();
            self.cursor = None;
            return;
        }
        let start = start.min(self.items.len() - 1);
        if self.shuffle {
            self.order = self.bag(Some(start));
            self.cursor = Some(0);
        } else {
            self.order = (0..self.items.len()).collect();
            self.cursor = Some(start);
        }
    }

    /// Adds items. Shuffled, they go to random places among the songs not heard yet this cycle.
    /// An idle queue starts at the first added item.
    pub fn enqueue(&mut self, items: Vec<QueueItem>) {
        let first_new = self.items.len();
        let count = items.len();
        self.items.extend(items);
        for index in first_new..first_new + count {
            if self.shuffle {
                let unheard_from = self.cursor.map_or(0, |c| c + 1);
                let at = self.rng.usize(unheard_from..=self.order.len());
                self.order.insert(at, index);
            } else {
                self.order.push(index);
            }
        }
        if self.cursor.is_none() && count > 0 {
            self.cursor = self.order.iter().position(|&i| i >= first_new);
        }
    }

    /// Moves on. `ended` says the current song finished on its own: with repeat-one, it plays
    /// again. Pressing Next (`ended == false`) always moves to another song. Past the end of the
    /// order, repeat-all (or repeat-one, when skipping) starts a new cycle; otherwise nothing is
    /// current.
    pub fn advance(&mut self, ended: bool) -> Option<&QueueItem> {
        if ended && self.repeat == RepeatMode::One && self.cursor.is_some() {
            return self.current();
        }
        let next = self.cursor.map_or(0, |c| c + 1);
        if next < self.order.len() {
            self.cursor = Some(next);
        } else if self.repeat != RepeatMode::Off && !self.items.is_empty() {
            self.new_cycle();
        } else {
            self.cursor = None;
        }
        self.current()
    }

    /// What would play after the current song ends on its own, without moving: for prefetching.
    /// `None` at the end of a shuffled cycle (the next cycle isn't shuffled yet) or of the queue.
    pub fn peek_next(&self) -> Option<&QueueItem> {
        let cursor = self.cursor?;
        if self.repeat == RepeatMode::One {
            return self.current();
        }
        match self.order.get(cursor + 1) {
            Some(&next) => Some(&self.items[next]),
            None if self.repeat == RepeatMode::All && !self.shuffle => self.order.first().map(|&i| &self.items[i]),
            None => None,
        }
    }

    /// Back to the song heard before this one, staying on the first.
    pub fn back(&mut self) -> Option<&QueueItem> {
        if let Some(c) = self.cursor {
            self.cursor = Some(c.saturating_sub(1));
        }
        self.current()
    }

    /// Plays item `index` next-and-now. Shuffled, it moves to just after what's been heard, so
    /// the unheard songs stay unheard; in list order it simply becomes current.
    pub fn jump(&mut self, index: usize) -> Option<&QueueItem> {
        if index >= self.items.len() {
            return self.current();
        }
        if !self.shuffle {
            self.cursor = Some(index);
            return self.current();
        }
        let from = self.order.iter().position(|&i| i == index).expect("every item is in the order");
        let mut heard = self.cursor.map_or(0, |c| c + 1);
        if from < heard {
            heard -= 1; // it was already heard this cycle; it's being heard again now
        }
        self.order.remove(from);
        self.order.insert(heard, index);
        self.cursor = Some(heard);
        self.current()
    }

    /// Removes an item. Returns `true` when it was the current one (playback must change).
    pub fn remove(&mut self, index: usize) -> bool {
        if index >= self.items.len() {
            return false;
        }
        self.items.remove(index);
        let from = self.order.iter().position(|&i| i == index).expect("every item is in the order");
        self.order.remove(from);
        for i in &mut self.order {
            if *i > index {
                *i -= 1;
            }
        }
        match self.cursor {
            Some(c) if c == from => {
                // The next song in the order slides into place; past the end, nothing is current.
                self.cursor = (from < self.order.len()).then_some(from);
                true
            }
            Some(c) if c > from => {
                self.cursor = Some(c - 1);
                false
            }
            _ => false,
        }
    }

    pub fn clear(&mut self) {
        self.items.clear();
        self.order.clear();
        self.cursor = None;
    }

    /// Starts the next cycle. Shuffled, a new bag that doesn't open with the song just played.
    fn new_cycle(&mut self) {
        let last = self.index();
        if self.shuffle {
            let mut order: Vec<usize> = (0..self.items.len()).collect();
            self.rng.shuffle(&mut order);
            if order.len() > 1 && Some(order[0]) == last {
                let swap = self.rng.usize(1..order.len());
                order.swap(0, swap);
            }
            self.order = order;
        }
        self.cursor = Some(0);
    }

    /// A shuffled order of every item, with `first` (if any) in front.
    fn bag(&mut self, first: Option<usize>) -> Vec<usize> {
        let mut rest: Vec<usize> = (0..self.items.len()).filter(|&i| Some(i) != first).collect();
        self.rng.shuffle(&mut rest);
        first.into_iter().chain(rest).collect()
    }
}

#[cfg(test)]
mod tests {
    use std::collections::BTreeSet;

    use shared::AudioRef;

    use super::*;

    fn item(n: usize) -> QueueItem {
        QueueItem {
            audio: AudioRef::Track { id: n.to_string() },
            title: format!("Song {n}"),
            artist: "Band".into(),
            duration: Some(180.0),
            video_id: None,
        }
    }

    fn items(n: usize) -> Vec<QueueItem> {
        (0..n).map(item).collect()
    }

    fn queue(seed: u64) -> Queue {
        Queue::with_rng(fastrand::Rng::with_seed(seed))
    }

    fn titles(queue: &Queue) -> Vec<&str> {
        queue.items().iter().map(|i| i.title.as_str()).collect()
    }

    /// Plays to the end of each song `count` times, returning which items played.
    fn play(queue: &mut Queue, count: usize) -> Vec<usize> {
        let mut heard = vec![queue.index().unwrap()];
        for _ in 1..count {
            queue.advance(true);
            heard.push(queue.index().unwrap());
        }
        heard
    }

    // --- In order ---

    #[test]
    fn replacing_starts_at_the_chosen_item_clamped_to_the_list() {
        let mut queue = queue(1);
        queue.replace(items(3), 1);
        assert_eq!(queue.current().unwrap().title, "Song 1");
        queue.replace(items(1), 9);
        assert_eq!(queue.index(), Some(0));
        queue.replace(vec![], 0);
        assert_eq!(queue.current(), None);
    }

    #[test]
    fn without_repeat_the_queue_stops_after_the_last_song() {
        let mut queue = queue(1);
        queue.replace(items(2), 0);
        assert_eq!(queue.advance(true).unwrap().title, "Song 1");
        assert!(queue.advance(true).is_none());
    }

    #[test]
    fn repeat_all_in_order_wraps_to_the_first_song() {
        let mut queue = queue(1);
        queue.set_repeat(RepeatMode::All);
        queue.replace(items(3), 0);
        assert_eq!(play(&mut queue, 7), [0, 1, 2, 0, 1, 2, 0]);
    }

    #[test]
    fn repeat_one_replays_when_a_song_ends_but_next_still_moves_on() {
        let mut queue = queue(1);
        queue.set_repeat(RepeatMode::One);
        queue.replace(items(3), 1);
        assert_eq!(queue.advance(true).unwrap().title, "Song 1");
        assert_eq!(queue.advance(true).unwrap().title, "Song 1");
        assert_eq!(queue.advance(false).unwrap().title, "Song 2");
        // Skipping past the end with repeat-one wraps rather than stopping.
        assert_eq!(queue.advance(false).unwrap().title, "Song 0");
    }

    #[test]
    fn peeking_shows_what_plays_next_without_moving() {
        let mut queue = queue(1);
        queue.replace(items(3), 1);
        assert_eq!(queue.peek_next().unwrap().title, "Song 2");
        assert_eq!(queue.index(), Some(1));
        queue.advance(true);
        assert!(queue.peek_next().is_none(), "end of the queue without repeat");
        queue.set_repeat(RepeatMode::All);
        assert_eq!(queue.peek_next().unwrap().title, "Song 0");
        queue.set_repeat(RepeatMode::One);
        assert_eq!(queue.peek_next().unwrap().title, "Song 2");
    }

    #[test]
    fn peeking_while_shuffled_follows_the_bag() {
        let mut queue = queue(5);
        queue.set_shuffle(true);
        queue.replace(items(5), 0);
        let expected = queue.order()[1];
        assert_eq!(queue.peek_next().map(|i| i.title.clone()), Some(format!("Song {expected}")));
        queue.advance(true);
        assert_eq!(queue.index(), Some(expected));
    }

    #[test]
    fn going_back_stops_at_the_first_song() {
        let mut queue = queue(1);
        queue.replace(items(2), 1);
        assert_eq!(queue.back().unwrap().title, "Song 0");
        assert_eq!(queue.back().unwrap().title, "Song 0");
    }

    #[test]
    fn enqueueing_into_an_idle_queue_starts_it() {
        let mut queue = queue(1);
        queue.enqueue(items(2));
        assert_eq!(queue.index(), Some(0));
        queue.enqueue(vec![item(2)]);
        assert_eq!(titles(&queue), ["Song 0", "Song 1", "Song 2"]);
    }

    #[test]
    fn removing_before_the_current_song_keeps_playing_it() {
        let mut queue = queue(1);
        queue.replace(items(3), 2);
        assert!(!queue.remove(0));
        assert_eq!(queue.current().unwrap().title, "Song 2");
    }

    #[test]
    fn removing_the_current_song_moves_to_the_next() {
        let mut queue = queue(1);
        queue.replace(items(3), 1);
        assert!(queue.remove(1));
        assert_eq!(queue.current().unwrap().title, "Song 2");
        assert!(queue.remove(1));
        assert_eq!(queue.current(), None);
    }

    // --- Shuffle ---

    #[test]
    fn shuffle_starts_with_the_chosen_song_and_plays_every_song_once() {
        for seed in 0..50 {
            let mut queue = queue(seed);
            queue.set_shuffle(true);
            queue.replace(items(8), 3);
            let heard = play(&mut queue, 8);
            assert_eq!(heard[0], 3, "seed {seed}: starts with the chosen song");
            assert_eq!(heard.iter().copied().collect::<BTreeSet<_>>().len(), 8, "seed {seed}: {heard:?}");
            assert!(queue.advance(true).is_none(), "seed {seed}: no repeat, so it stops");
        }
    }

    /// The bug in other players: shuffle + repeat replaying songs already heard while others
    /// haven't played yet. Here every cycle plays every song exactly once.
    #[test]
    fn shuffle_with_repeat_plays_the_whole_bag_before_any_song_again() {
        for seed in 0..50 {
            let mut queue = queue(seed);
            queue.set_shuffle(true);
            queue.set_repeat(RepeatMode::All);
            queue.replace(items(6), 0);
            let heard = play(&mut queue, 6 * 5);
            for (n, cycle) in heard.chunks(6).enumerate() {
                let distinct: BTreeSet<_> = cycle.iter().collect();
                assert_eq!(distinct.len(), 6, "seed {seed}, cycle {n}: {cycle:?}");
            }
            for pair in heard.windows(2) {
                assert_ne!(pair[0], pair[1], "seed {seed}: a song played twice in a row across cycles");
            }
        }
    }

    #[test]
    fn songs_added_mid_cycle_join_the_unheard_part() {
        for seed in 0..50 {
            let mut queue = queue(seed);
            queue.set_shuffle(true);
            queue.set_repeat(RepeatMode::All);
            queue.replace(items(4), 0);
            let mut heard = play(&mut queue, 3);
            queue.enqueue(vec![item(4), item(5)]);
            // The rest of this cycle: the one unheard original plus both new songs.
            for _ in 0..3 {
                heard.push(queue.advance(true).unwrap().title.trim_start_matches("Song ").parse().unwrap());
            }
            assert_eq!(heard.iter().collect::<BTreeSet<_>>().len(), 6, "seed {seed}: {heard:?}");
        }
    }

    #[test]
    fn jumping_while_shuffled_keeps_the_unheard_songs_unheard() {
        let mut queue = queue(7);
        queue.set_shuffle(true);
        queue.replace(items(6), 0);
        let unheard_before: BTreeSet<_> = queue.order()[1..].iter().copied().collect();
        let target = *unheard_before.iter().next().unwrap();

        queue.jump(target);

        assert_eq!(queue.index(), Some(target));
        let rest: BTreeSet<_> = queue.order()[2..].iter().copied().collect();
        let expected: BTreeSet<_> = unheard_before.into_iter().filter(|&i| i != target).collect();
        assert_eq!(rest, expected);
        // Back goes to the song actually heard before.
        queue.back();
        assert_eq!(queue.index(), Some(0));
    }

    #[test]
    fn turning_shuffle_off_returns_to_list_order_from_the_current_song() {
        let mut queue = queue(3);
        queue.set_shuffle(true);
        queue.replace(items(5), 2);
        queue.advance(true);
        let current = queue.index().unwrap();

        queue.set_shuffle(false);

        assert_eq!(queue.index(), Some(current));
        assert_eq!(queue.order(), [0, 1, 2, 3, 4]);
        if current + 1 < 5 {
            queue.advance(true);
            assert_eq!(queue.index(), Some(current + 1));
        }
    }

    #[test]
    fn removing_while_shuffled_keeps_the_order_consistent() {
        let mut queue = queue(11);
        queue.set_shuffle(true);
        queue.replace(items(5), 0);
        queue.remove(3);
        let mut order = queue.order().to_vec();
        order.sort_unstable();
        assert_eq!(order, [0, 1, 2, 3]);
        assert_eq!(queue.items().len(), 4);
    }
}
