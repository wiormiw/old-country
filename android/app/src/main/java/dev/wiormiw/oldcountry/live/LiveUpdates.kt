package dev.wiormiw.oldcountry.live

import android.util.Log
import androidx.annotation.Keep
import com.microsoft.signalr.HubConnection
import com.microsoft.signalr.HubConnectionBuilder
import com.microsoft.signalr.HubConnectionState
import com.microsoft.signalr.TransportEnum
import dev.wiormiw.oldcountry.data.ApiClient
import dev.wiormiw.oldcountry.data.ApiException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.launch
import kotlinx.coroutines.rx3.await
import kotlinx.coroutines.suspendCancellableCoroutine
import okhttp3.OkHttpClient
import kotlin.coroutines.resume
import kotlin.random.Random
import kotlin.time.Duration
import kotlin.time.Duration.Companion.milliseconds

/** What changed on the server: a hint to refetch, not the data. Mirrors the server's LibraryChanges. */
data class LibraryChanges(
    val playlists: Set<String> = emptySet(),
    val tracks: Boolean = false,
    val downloads: Boolean = false,
    /** Refetch everything: the server fell behind, or the app was offline and may have missed changes. */
    val everything: Boolean = false,
) {
    fun touchesPlaylist(id: String) = everything || id in playlists
    val touchesPlaylistList get() = everything || playlists.isNotEmpty()
    val touchesTracks get() = everything || tracks
    val touchesDownloads get() = everything || downloads

    companion object {
        val Everything = LibraryChanges(everything = true)
    }
}

/** The hub message as Gson reads it (it fills fields by name, so no Kotlin defaults or val). */
@Keep
internal class ChangesMessage {
    var playlists: List<String>? = null
    var tracks: Boolean = false
    var downloads: Boolean = false
    var everything: Boolean = false

    fun toChanges() = LibraryChanges(playlists.orEmpty().toSet(), tracks, downloads, everything)
}

/** News from the server worth showing: "Downloaded “Song”", "“Mix”: 3 new songs from YouTube". */
data class Announcement(val kind: String, val message: String, val trackId: String?, val playlistId: String?)

/** An announcement as Gson reads it (see [ChangesMessage]). */
@Keep
internal class AnnouncementMessage {
    var kind: String? = null
    var message: String? = null
    var trackId: String? = null
    var playlistId: String? = null

    fun toAnnouncement() = message?.let { Announcement(kind.orEmpty(), it, trackId, playlistId) }
}

enum class LiveStatus { Off, Connecting, Live, Reconnecting }

/**
 * Keeps a SignalR connection to the server's `/hubs/library` while the app is on screen, and
 * passes on what changed so screens refresh themselves.
 *
 * The Java client has no automatic reconnect (the .NET and JavaScript ones do), so this is the
 * supervisor: it connects, waits for the connection to close, and tries again with exponential
 * backoff plus jitter (so phones on the same Wi-Fi don't all retry in step), until stopped. It
 * starts over when the server address changes. After every (re)connect it says "everything
 * changed": whatever happened while it was away, screens catch up.
 */
class LiveUpdates(
    private val http: OkHttpClient,
    private val serverUrl: Flow<String>,
    private val scope: CoroutineScope,
) {
    private val _changes = MutableSharedFlow<LibraryChanges>(extraBufferCapacity = 16, onBufferOverflow = BufferOverflow.DROP_OLDEST)
    val changes: SharedFlow<LibraryChanges> = _changes.asSharedFlow()

    private val _news = MutableSharedFlow<Announcement>(extraBufferCapacity = 16, onBufferOverflow = BufferOverflow.DROP_OLDEST)
    val news: SharedFlow<Announcement> = _news.asSharedFlow()

    private val _status = MutableStateFlow(LiveStatus.Off)
    val status: StateFlow<LiveStatus> = _status.asStateFlow()

    private var job: Job? = null

    /** Connects (and keeps reconnecting) until [stop]. Calling it again while running does nothing. */
    fun start() {
        if (job?.isActive == true) return
        job = scope.launch(Dispatchers.IO) {
            serverUrl.distinctUntilChanged().collectLatest { url -> superviseConnection(url) }
        }
    }

    fun stop() {
        job?.cancel()
        job = null
        _status.value = LiveStatus.Off
    }

    private suspend fun superviseConnection(url: String) {
        val hubUrl = try {
            ApiClient.parseBase(url).newBuilder().addPathSegments(HUB_PATH).build().toString()
        } catch (_: ApiException) {
            _status.value = LiveStatus.Off // no server set yet; Settings will change the flow
            return
        }
        var failures = 0
        while (true) {
            _status.value = if (failures == 0) LiveStatus.Connecting else LiveStatus.Reconnecting
            val connection = build(hubUrl)
            try {
                connection.start().await()
                Log.i(TAG, "live updates connected to $hubUrl")
                failures = 0
                _status.value = LiveStatus.Live
                _changes.tryEmit(LibraryChanges.Everything) // catch up on anything missed
                val reason = connection.closed()
                Log.i(TAG, "live updates closed: ${reason?.message ?: "by the server"}")
            } catch (e: Exception) {
                if (e is kotlinx.coroutines.CancellationException) {
                    connection.stop()
                    throw e
                }
                Log.d(TAG, "live updates couldn't connect: ${e.message}")
            } finally {
                if (connection.connectionState != HubConnectionState.DISCONNECTED) connection.stop()
            }
            failures++
            _status.value = LiveStatus.Reconnecting
            delay(backoff(failures))
        }
    }

    private fun build(hubUrl: String): HubConnection {
        val connection = HubConnectionBuilder.create(hubUrl)
            // Straight to WebSockets: one round trip less than negotiating, and the server allows it.
            .withTransport(TransportEnum.WEBSOCKETS)
            .shouldSkipNegotiate(true)
            // The app's own client: its User-Agent and timeouts. The read timeout must go (0 = none),
            // since a WebSocket sits idle between messages; the hub's pings detect dead links.
            .setHttpClientBuilderCallback { builder -> builder.connectionPool(http.connectionPool).readTimeout(0, java.util.concurrent.TimeUnit.MILLISECONDS) }
            // Match the server: it pings every 15 s; 30 s of silence means the link is dead.
            .withServerTimeout(30_000)
            .withKeepAliveInterval(15_000)
            .withHandshakeResponseTimeout(10_000)
            .build()
        connection.on("Changed", { message: ChangesMessage -> _changes.tryEmit(message.toChanges()) }, ChangesMessage::class.java)
        connection.on(
            "Announced",
            { list: Array<AnnouncementMessage> -> list.forEach { it.toAnnouncement()?.let(_news::tryEmit) } },
            Array<AnnouncementMessage>::class.java,
        )
        return connection
    }

    companion object {
        private const val TAG = "LiveUpdates"
        private const val HUB_PATH = "hubs/library"

        /** 1 s, 2 s, 4 s … up to 30 s, each with up to 30 % added at random. */
        internal fun backoff(failures: Int, random: Random = Random.Default): Duration {
            val base = (1_000L shl (failures - 1).coerceIn(0, 5)).coerceAtMost(30_000)
            return (base + random.nextLong(0, base * 3 / 10 + 1)).milliseconds
        }
    }
}

/** Suspends until the connection closes; the exception it closed with, if any. */
private suspend fun HubConnection.closed(): Exception? = suspendCancellableCoroutine { continuation ->
    onClosed { error -> if (continuation.isActive) continuation.resume(error) }
    // It may have closed before the handler was set.
    if (connectionState == HubConnectionState.DISCONNECTED && continuation.isActive) continuation.resume(null)
    continuation.invokeOnCancellation { stop() }
}
