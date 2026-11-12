//! The play queue: which items, which is current, and how next/previous/remove move through it.
//! Pure logic, so its rules are tested without audio.

use shared::QueueItem;

#[derive(Debug, Default, Clone)]
pub struct Queue {
    items: Vec<QueueItem>,
    index: Option<usize>,
}

impl Queue {
    pub fn items(&self) -> &[QueueItem] {
        &self.items
    }

    pub fn index(&self) -> Option<usize> {
        self.index
    }

    pub fn current(&self) -> Option<&QueueItem> {
        self.index.and_then(|i| self.items.get(i))
    }

    /// Replaces the queue and starts at `start` (clamped to the list).
    pub fn replace(&mut self, items: Vec<QueueItem>, start: usize) {
        self.index = (!items.is_empty()).then(|| start.min(items.len() - 1));
        self.items = items;
    }

    /// Adds to the end; an empty queue starts at the first added item.
    pub fn enqueue(&mut self, items: Vec<QueueItem>) {
        let was_empty = self.items.is_empty();
        self.items.extend(items);
        if was_empty && !self.items.is_empty() {
            self.index = Some(0);
        }
    }

    /// Moves to the next item; `None` (and no current item) past the end.
    pub fn advance(&mut self) -> Option<&QueueItem> {
        let next = self.index.map_or(0, |i| i + 1);
        self.index = (next < self.items.len()).then_some(next);
        self.current()
    }

    /// Moves to the previous item, staying on the first.
    pub fn back(&mut self) -> Option<&QueueItem> {
        if let Some(i) = self.index {
            self.index = Some(i.saturating_sub(1));
        }
        self.current()
    }

    pub fn jump(&mut self, index: usize) -> Option<&QueueItem> {
        if index < self.items.len() {
            self.index = Some(index);
        }
        self.current()
    }

    /// Removes an item. Returns `true` when it was the current one (playback must change).
    pub fn remove(&mut self, index: usize) -> bool {
        if index >= self.items.len() {
            return false;
        }
        self.items.remove(index);
        match self.index {
            Some(current) if current == index => {
                // The item after it slides into place; past the end there is nothing current.
                self.index = (index < self.items.len()).then_some(index);
                true
            }
            Some(current) if current > index => {
                self.index = Some(current - 1);
                false
            }
            _ => false,
        }
    }

    pub fn clear(&mut self) {
        self.items.clear();
        self.index = None;
    }
}

#[cfg(test)]
mod tests {
    use shared::AudioRef;

    use super::*;

    fn item(n: u32) -> QueueItem {
        QueueItem {
            audio: AudioRef::Track { id: n.to_string() },
            title: format!("Song {n}"),
            artist: "Band".into(),
            duration: Some(180.0),
        }
    }

    fn titles(queue: &Queue) -> Vec<&str> {
        queue.items().iter().map(|i| i.title.as_str()).collect()
    }

    #[test]
    fn replacing_starts_at_the_chosen_item_clamped_to_the_list() {
        let mut queue = Queue::default();
        queue.replace(vec![item(1), item(2), item(3)], 1);
        assert_eq!(queue.current().unwrap().title, "Song 2");

        queue.replace(vec![item(1)], 9);
        assert_eq!(queue.index(), Some(0));

        queue.replace(vec![], 0);
        assert_eq!(queue.current(), None);
    }

    #[test]
    fn advancing_past_the_end_leaves_nothing_current() {
        let mut queue = Queue::default();
        queue.replace(vec![item(1), item(2)], 0);
        assert_eq!(queue.advance().unwrap().title, "Song 2");
        assert!(queue.advance().is_none());
        assert_eq!(queue.index(), None);
    }

    #[test]
    fn going_back_stops_at_the_first_item() {
        let mut queue = Queue::default();
        queue.replace(vec![item(1), item(2)], 1);
        assert_eq!(queue.back().unwrap().title, "Song 1");
        assert_eq!(queue.back().unwrap().title, "Song 1");
    }

    #[test]
    fn enqueueing_into_an_empty_queue_makes_the_first_item_current() {
        let mut queue = Queue::default();
        queue.enqueue(vec![item(1), item(2)]);
        assert_eq!(queue.index(), Some(0));
        queue.enqueue(vec![item(3)]);
        assert_eq!(titles(&queue), ["Song 1", "Song 2", "Song 3"]);
        assert_eq!(queue.index(), Some(0));
    }

    #[test]
    fn removing_before_the_current_item_keeps_playing_the_same_song() {
        let mut queue = Queue::default();
        queue.replace(vec![item(1), item(2), item(3)], 2);
        assert!(!queue.remove(0));
        assert_eq!(queue.current().unwrap().title, "Song 3");
    }

    #[test]
    fn removing_the_current_item_moves_to_the_one_after() {
        let mut queue = Queue::default();
        queue.replace(vec![item(1), item(2), item(3)], 1);
        assert!(queue.remove(1));
        assert_eq!(queue.current().unwrap().title, "Song 3");

        assert!(queue.remove(1));
        assert_eq!(queue.current(), None);
        assert_eq!(titles(&queue), ["Song 1"]);
    }

    #[test]
    fn jumping_outside_the_queue_changes_nothing() {
        let mut queue = Queue::default();
        queue.replace(vec![item(1), item(2)], 0);
        queue.jump(5);
        assert_eq!(queue.index(), Some(0));
        assert_eq!(queue.jump(1).unwrap().title, "Song 2");
    }
}
