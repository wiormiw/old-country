package dev.wiormiw.oldcountry.data

import kotlinx.coroutines.test.runTest
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import okhttp3.OkHttpClient
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.fail
import org.junit.Before
import org.junit.Test

class ApiClientTest {
    private val server = MockWebServer()
    private lateinit var api: ApiClient

    @Before
    fun start() {
        server.start()
        api = ApiClient(OkHttpClient()) { server.url("/").toString() }
    }

    @After
    fun stop() = server.close()

    private fun respond(code: Int, body: String = "") =
        server.enqueue(MockResponse.Builder().code(code).addHeader("Content-Type", "application/json").body(body).build())

    @Test
    fun `queries are encoded and bodies parsed`() = runTest {
        respond(200, """[{"videoId":"v1","title":"Song","isAvailableOffline":true,"trackId":"t1"}]""")

        val results = api.youTubeSearch("old & country", 5)

        assertEquals("t1", results.single().trackId)
        val request = server.takeRequest()
        assertEquals("/api/youtube-music/search?query=old%20%26%20country&limit=5", request.target)
    }

    @Test
    fun `json bodies are sent`() = runTest {
        respond(201, """{"id":"p1","name":"Road","trackCount":0,"createdAt":"x"}""")

        api.createPlaylist("Road", null)

        val request = server.takeRequest()
        assertEquals("POST", request.method)
        assertEquals("""{"name":"Road","description":null}""", request.body?.utf8())
    }

    @Test
    fun `problem details become api errors with field messages`() = runTest {
        respond(400, """{"title":"One or more validation errors occurred.","status":400,"code":"validation","errors":{"name":["Give the playlist a name."]}}""")

        val error = try {
            api.createPlaylist("", null)
            fail("expected an error")
            return@runTest
        } catch (e: ApiException) {
            e
        }
        assertEquals("validation", error.code)
        assertEquals("One or more validation errors occurred.", error.message)
        assertEquals(listOf("Give the playlist a name."), error.fields["name"])
    }

    @Test
    fun `no content answers are success`() = runTest {
        respond(204)
        api.removeFromPlaylist("p1", 2)
        assertEquals("/api/playlists/p1/tracks/2", server.takeRequest().target)
    }

    @Test
    fun `an unset server says to configure it`() = runTest {
        val unset = ApiClient(OkHttpClient()) { "" }
        val error = runCatching { unset.playlists() }.exceptionOrNull() as ApiException
        assertEquals("server.not_configured", error.code)
    }

    @Test
    fun `an unreachable server is reported as such`() = runTest {
        val gone = ApiClient(OkHttpClient()) { "http://127.0.0.1:1" }
        val error = runCatching { gone.playlists() }.exceptionOrNull() as ApiException
        assertEquals("server.unreachable", error.code)
    }

    @Test
    fun `the health check rejects a server that is not healthy`() = runTest {
        respond(503)
        val error = runCatching { api.checkServer(server.url("/").toString()) }.exceptionOrNull() as ApiException
        assertEquals("server.unhealthy", error.code)
        assertEquals("/health", server.takeRequest().target)
    }

    @Test
    fun `addresses without a scheme get http`() {
        assertEquals("http://192.168.1.10:5000/", ApiClient.parseBase("192.168.1.10:5000/").toString())
        assertThrows(ApiException::class.java) { ApiClient.parseBase("http://") }
    }
}
