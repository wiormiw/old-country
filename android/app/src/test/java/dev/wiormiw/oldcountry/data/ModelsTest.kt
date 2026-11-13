package dev.wiormiw.oldcountry.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ModelsTest {
    @Test
    fun `a track page parses`() {
        val page = json.decodeFromString<Paged<Track>>(
            """{"items":[{"id":"0193f4a2-0000-7000-8000-000000000001","title":"Dust Road","artist":"Old Country Band",
            "duration":"00:03:35","isAvailableOffline":true,"codec":"Flac","createdAt":"2026-11-12T06:33:07.123+00:00"}],
            "page":1,"pageSize":50,"totalCount":1,"totalPages":1}""",
        )
        val track = page.items.single()
        assertEquals(215.0, track.duration, 0.0)
        assertEquals("Flac", track.codec)
        assertNull(track.youTubeVideoId)
        assertEquals(1, page.totalPages)
    }

    @Test
    fun `a search result without optional fields parses`() {
        val result = json.decodeFromString<YouTubeResult>("""{"videoId":"dQw4w9WgXcQ","title":"Never Gonna Give You Up","isAvailableOffline":false}""")
        assertNull(result.duration)
        assertNull(result.artist)
    }

    @Test
    fun `optional durations parse when present`() {
        val result = json.decodeFromString<YouTubeResult>("""{"videoId":"v","title":"T","duration":"00:04:01","isAvailableOffline":false}""")
        assertEquals(241.0, result.duration!!, 0.0)
    }

    @Test
    fun `a validation problem keeps its field errors`() {
        val problem = json.decodeFromString<ProblemDetails>(
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.",
            "status":400,"errors":{"title":["Give the track a title."]},"code":"validation","traceId":"00-abc"}""",
        )
        assertEquals("validation", problem.code)
        assertEquals(listOf("Give the track a title."), problem.errors["title"])
    }

    @Test
    fun `enums use the server's names`() {
        val status = json.decodeFromString<DownloadStatus>(
            """{"jobId":"j","videoId":"dQw4w9WgXcQ","state":"Completed","trackId":"t","queuedAt":"x","finishedAt":"y"}""",
        )
        assertEquals(DownloadState.Completed, status.state)
    }

    @Test
    fun `durations round trip through TimeSpan text`() {
        val entry = PlaylistEntry(0, "t", "T", "A", 4_000.0, false, null, "x")
        val text = json.encodeToString(PlaylistEntry.serializer(), entry)
        assert(text.contains(""""duration":"01:06:40""""))
        assertEquals(entry, json.decodeFromString<PlaylistEntry>(text))
    }

    @Test
    fun `TimeSpan handles days, fractions and signs`() {
        assertEquals(93_784.5, TimeSpan.parse("1.02:03:04.5000000")!!, 1e-9)
        assertEquals(-60.0, TimeSpan.parse("-00:01:00")!!, 0.0)
        assertNull(TimeSpan.parse("3:35"))
        assertEquals("1.02:03:05", TimeSpan.format(93_784.5))
        assertEquals("3:35", TimeSpan.display(215.4))
        assertEquals("1:02:03", TimeSpan.display(3_723.0))
        assertEquals("–:––", TimeSpan.display(null))
    }

    @Test
    fun `import reports say whether they synced, and older servers still parse`() {
        val sync = json.decodeFromString<ImportReport>(
            """{"playlistId":"p","name":"Mix","isNew":false,"entries":68,"added":3,"tracksCreated":3,"tracksExisting":0,"skipped":0}""",
        )
        assertEquals("Updated \"Mix\" from YouTube: 3 new songs added (68 in total).", importMessage(sync))
        val older = json.decodeFromString<ImportReport>(
            """{"playlistId":"p","name":"Mix","entries":65,"tracksCreated":65,"tracksExisting":0,"skipped":1}""",
        )
        assertEquals("Imported \"Mix\": 65 songs, 65 new to the library, 1 skipped.", importMessage(older))
        assertEquals("\"Mix\" is already up to date with YouTube.", importMessage(sync.copy(added = 0)))
    }
}
