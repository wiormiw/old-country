package dev.wiormiw.oldcountry.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.serialization.KSerializer
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import kotlinx.serialization.json.putJsonArray
import kotlinx.serialization.json.add
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.serializer
import okhttp3.Call
import okhttp3.Callback
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import java.io.IOException
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/** Every failure, in one shape the screens can show. */
class ApiException(
    /** The server's code (`track.not_found`, `validation`) or ours (`server.unreachable`, `server.not_configured`). */
    val code: String,
    override val message: String,
    /** Validation messages by field. */
    val fields: Map<String, List<String>> = emptyMap(),
) : Exception(message)

val json = Json {
    ignoreUnknownKeys = true
    explicitNulls = false
}

/** The old-country server's API. `baseUrl` is read on every call, so Settings changes apply at once. */
class ApiClient(private val http: OkHttpClient, private val baseUrl: () -> String) {

    suspend fun checkServer(url: String) {
        val base = parseBase(url)
        val response = execute(Request.Builder().url(base.newBuilder().addPathSegment("health").build()).build())
        withContext(Dispatchers.IO) { response.close() }
        if (!response.isSuccessful) {
            throw ApiException("server.unhealthy", "The server answered, but isn't healthy (HTTP ${response.code}).")
        }
    }

    suspend fun tracks(page: Int, pageSize: Int, search: String?): Paged<Track> =
        get("api/tracks", listOfNotNull("page" to "$page", "pageSize" to "$pageSize", search?.let { "search" to it }))

    suspend fun track(id: String): TrackDetail = get("api/tracks/$id")
    suspend fun scanLibrary(): ScanStatus = send("POST", "api/tracks/scans")
    suspend fun scanStatus(): ScanStatus = get("api/tracks/scans/latest")

    suspend fun playlists(): List<PlaylistSummary> = get("api/playlists", serializer = ListSerializer(PlaylistSummary.serializer()))
    suspend fun playlist(id: String): Playlist = get("api/playlists/$id")

    suspend fun createPlaylist(name: String, description: String?): PlaylistSummary =
        send("POST", "api/playlists", buildJsonObject { put("name", name); put("description", description) })

    suspend fun updatePlaylist(id: String, name: String, description: String?): PlaylistSummary =
        send("PUT", "api/playlists/$id", buildJsonObject { put("name", name); put("description", description) })

    suspend fun deletePlaylist(id: String) = sendEmpty("DELETE", "api/playlists/$id")

    suspend fun addToPlaylist(id: String, trackIds: List<String>): PlaylistSummary =
        send("POST", "api/playlists/$id/tracks", buildJsonObject { putJsonArray("trackIds") { trackIds.forEach { add(it) } } })

    suspend fun removeFromPlaylist(id: String, position: Int) = sendEmpty("DELETE", "api/playlists/$id/tracks/$position")

    suspend fun moveInPlaylist(id: String, from: Int, to: Int) =
        sendEmpty("POST", "api/playlists/$id/tracks/move", buildJsonObject { put("from", from); put("to", to) })

    suspend fun youTubeSearch(query: String, limit: Int): List<YouTubeResult> =
        get("api/youtube-music/search", listOf("query" to query, "limit" to "$limit"), ListSerializer(YouTubeResult.serializer()))

    suspend fun download(videoIds: List<String>): List<DownloadStatus> = send(
        "POST", "api/youtube-music/downloads",
        buildJsonObject { putJsonArray("videoIds") { videoIds.forEach { add(it) } } },
        ListSerializer(DownloadStatus.serializer()),
    )

    suspend fun downloads(): Downloads = get("api/youtube-music/downloads")

    suspend fun downloadPlaylist(id: String, retrySkipped: Boolean): PlaylistDownloadReport =
        send("POST", "api/youtube-music/playlists/$id/downloads", buildJsonObject { put("retrySkipped", retrySkipped) })

    suspend fun syncPlaylist(id: String): ImportReport = send("POST", "api/youtube-music/playlists/$id/sync")

    suspend fun import(playlist: String): ImportReport =
        send("POST", "api/youtube-music/imports", buildJsonObject { put("playlist", playlist) })

    // --- plumbing

    private suspend inline fun <reified T> get(
        path: String,
        query: List<Pair<String, String>> = emptyList(),
        serializer: KSerializer<T> = serializer(),
    ): T = body(request("GET", path, query, null), serializer)

    private suspend inline fun <reified T> send(
        method: String,
        path: String,
        body: JsonElement? = null,
        serializer: KSerializer<T> = serializer(),
    ): T = body(request(method, path, emptyList(), body), serializer)

    private suspend fun sendEmpty(method: String, path: String, body: JsonElement? = null) {
        val response = request(method, path, emptyList(), body)
        withContext(Dispatchers.IO) { response.close() }
    }

    private suspend fun <T> body(response: Response, serializer: KSerializer<T>): T {
        val text = withContext(Dispatchers.IO) { response.use { it.body.string() } }
        return try {
            json.decodeFromString(serializer, text)
        } catch (e: IllegalArgumentException) {
            throw ApiException("server.unexpected", "The server's answer couldn't be read: ${e.message}")
        }
    }

    private suspend fun request(method: String, path: String, query: List<Pair<String, String>>, body: JsonElement?): Response {
        val url = parseBase(baseUrl()).newBuilder().addPathSegments(path).apply {
            query.forEach { (k, v) -> addQueryParameter(k, v) }
        }.build()
        val requestBody: RequestBody? = when {
            body != null -> body.toString().toRequestBody(JSON)
            method == "GET" || method == "DELETE" -> null
            else -> ByteArray(0).toRequestBody(null)
        }
        val response = execute(Request.Builder().url(url).method(method, requestBody).build())
        if (!response.isSuccessful) throw withContext(Dispatchers.IO) { response.use { problem(it) } }
        return response
    }

    // On the IO dispatcher: a cancelled call closes its response, which is network work Android
    // forbids on the main thread.
    private suspend fun execute(request: Request): Response = try {
        withContext(Dispatchers.IO) { http.newCall(request).await() }
    } catch (e: IOException) {
        throw ApiException("server.unreachable", "Can't reach the server at ${request.url.scheme}://${request.url.host}:${request.url.port}. Is it running, and on this network?")
    }

    companion object {
        private val JSON = "application/json".toMediaType()

        /** The server's base URL, or why it isn't one. */
        fun parseBase(url: String): HttpUrl {
            val trimmed = url.trim()
            if (trimmed.isEmpty()) throw ApiException("server.not_configured", "Set the server address in Settings first.")
            val withScheme = if (Regex("^[a-zA-Z][a-zA-Z0-9+.-]*://").containsMatchIn(trimmed)) trimmed else "http://$trimmed"
            return withScheme.trimEnd('/').toHttpUrlOrNull()
                ?: throw ApiException("server.invalid_url", "\"$url\" isn't a server address. Try http://192.168.1.10:5000.")
        }

        internal fun problem(response: Response): ApiException {
            val text = runCatching { response.body.string() }.getOrDefault("")
            val details = runCatching { json.decodeFromString(ProblemDetails.serializer(), text) }.getOrNull()
            val message = details?.detail ?: details?.title ?: "The server answered HTTP ${response.code}."
            return ApiException(details?.code ?: "http.${response.code}", message, details?.errors.orEmpty())
        }
    }
}

private suspend fun Call.await(): Response = suspendCancellableCoroutine { continuation ->
    continuation.invokeOnCancellation { cancel() }
    enqueue(object : Callback {
        override fun onResponse(call: Call, response: Response) = continuation.resume(response) { _, value, _ -> value.close() }
        override fun onFailure(call: Call, e: IOException) = continuation.resumeWithException(e)
    })
}
