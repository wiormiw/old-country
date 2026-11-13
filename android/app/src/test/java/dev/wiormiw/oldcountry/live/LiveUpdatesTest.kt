package dev.wiormiw.oldcountry.live

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.random.Random
import kotlin.time.Duration.Companion.seconds

class LiveUpdatesTest {
    @Test
    fun `retries back off exponentially to 30 seconds, with at most 30 percent jitter`() {
        val none = object : Random() {
            override fun nextBits(bitCount: Int) = 0
        }
        assertEquals(1.seconds, LiveUpdates.backoff(1, none))
        assertEquals(2.seconds, LiveUpdates.backoff(2, none))
        assertEquals(16.seconds, LiveUpdates.backoff(5, none))
        assertEquals(30.seconds, LiveUpdates.backoff(6, none))
        assertEquals(30.seconds, LiveUpdates.backoff(60, none))
        repeat(200) {
            val wait = LiveUpdates.backoff(10, Random(it))
            assertTrue(wait in 30.seconds..39.seconds)
        }
    }

    @Test
    fun `a hub message becomes changes, and screens ask the right questions`() {
        val message = ChangesMessage().apply {
            playlists = listOf("p1")
            tracks = false
            downloads = true
        }
        val changes = message.toChanges()
        assertTrue(changes.touchesPlaylist("p1"))
        assertFalse(changes.touchesPlaylist("p2"))
        assertTrue(changes.touchesPlaylistList)
        assertFalse(changes.touchesTracks)
        assertTrue(changes.touchesDownloads)

        val everything = LibraryChanges.Everything
        assertTrue(everything.touchesPlaylist("any") && everything.touchesTracks && everything.touchesDownloads)
        assertFalse(ChangesMessage().toChanges().touchesPlaylistList)
    }
}
