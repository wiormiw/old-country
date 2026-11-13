package dev.wiormiw.oldcountry.playback

import dev.wiormiw.oldcountry.data.Track
import dev.wiormiw.oldcountry.data.YouTubeResult
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class AudioRefTest {
    @Test
    fun `keys round trip and paths match the server's routes`() {
        val track = AudioRef.Track("0193f4a2")
        val video = AudioRef.YouTube("dQw4w9WgXcQ")
        assertEquals(track, AudioRef.fromKey(track.key))
        assertEquals(video, AudioRef.fromKey(video.key))
        assertEquals("/api/tracks/0193f4a2/audio", track.path)
        assertEquals("/api/youtube-music/videos/dQw4w9WgXcQ/audio", video.path)
        assertNull(AudioRef.fromKey("something-else"))
    }

    @Test
    fun `search results play the server's copy when it has one`() {
        val downloaded = YouTubeResult("v1", "T", null, 61.0, null, "t1", true)
        assertEquals(AudioRef.Track("t1"), downloaded.toQueueItem().audio)
        assertEquals(AudioRef.YouTube("v1"), downloaded.copy(isAvailableOffline = false).toQueueItem().audio)
        assertEquals(AudioRef.YouTube("v1"), downloaded.copy(trackId = null, isAvailableOffline = false).toQueueItem().audio)
        assertEquals("v1", downloaded.toQueueItem().videoId)
    }

    @Test
    fun `library songs not downloaded yet stream from YouTube`() {
        val track = Track("t1", "T", "A", 61.0, isAvailableOffline = false, youTubeVideoId = "v1", createdAt = "x")
        assertEquals(AudioRef.YouTube("v1"), track.toQueueItem().audio)
        assertEquals(AudioRef.Track("t1"), track.copy(isAvailableOffline = true).toQueueItem().audio)
        assertEquals(AudioRef.Track("t1"), track.copy(youTubeVideoId = null).toQueueItem().audio)
    }
}
