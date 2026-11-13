package dev.wiormiw.oldcountry.data

import org.junit.Assert.assertEquals
import org.junit.Test
import java.time.OffsetDateTime

class LabelsTest {
    private val now = OffsetDateTime.parse("2026-11-13T12:00:00Z")

    @Test
    fun `time ago reads like the desktop`() {
        assertEquals("just now", timeAgo("2026-11-13T11:59:40Z", now))
        assertEquals("5 min ago", timeAgo("2026-11-13T11:55:00+00:00", now))
        assertEquals("3 h ago", timeAgo("2026-11-13T09:00:00Z", now))
        assertEquals("yesterday", timeAgo("2026-11-12T08:00:00Z", now))
        assertEquals("4 days ago", timeAgo("2026-11-09T12:00:00Z", now))
        assertEquals("not a date", timeAgo("not a date", now))
    }

    @Test
    fun `download all says what it queued`() {
        assertEquals(
            "3 songs queued for download, 2 already on the way, 1 skipped (refused by YouTube before; use Retry skipped). See Downloads for progress.",
            playlistDownloadMessage(PlaylistDownloadReport(queued = 3, alreadyDownloaded = 9, alreadyQueued = 2, skipped = 1, notOnYouTube = 0)),
        )
        assertEquals(
            "Nothing new to download. See Downloads for progress.",
            playlistDownloadMessage(PlaylistDownloadReport(0, 5, 0, 0, 0)),
        )
    }

    @Test
    fun `refusals have short labels`() {
        assertEquals("Premium only", problemLabel("PremiumOnly"))
        assertEquals("Download failed", problemLabel("Failed"))
        assertEquals(false, isLastingProblem("Failed"))
        assertEquals("Gave up after 3 tries", errorCodeLabel("download.gave_up"))
        assertEquals(null, errorCodeLabel("something.else"))
    }
}
