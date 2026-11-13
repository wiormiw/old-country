package dev.wiormiw.oldcountry.playback

import android.content.ComponentName
import android.content.Context
import androidx.media3.common.Player
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import dev.wiormiw.oldcountry.data.RepeatMode
import com.google.common.util.concurrent.ListenableFuture
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.guava.await
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/** What the screens draw the player from. */
data class PlayerUi(
    val queue: List<QueueItem> = emptyList(),
    val index: Int? = null,
    val playing: Boolean = false,
    /** Fetching audio before playback can start (or continue after a seek). */
    val buffering: Boolean = false,
    val position: Double = 0.0,
    val duration: Double? = null,
    val shuffle: Boolean = false,
    val repeat: RepeatMode = RepeatMode.Off,
    /** Why the current song can't play, if it can't. */
    val error: String? = null,
) {
    val current: QueueItem? get() = index?.let(queue::getOrNull)
}

/** The screens' handle on the playback service. */
class PlayerConnection(private val context: Context, private val scope: CoroutineScope) {
    private val _state = MutableStateFlow(PlayerUi())
    val state: StateFlow<PlayerUi> = _state.asStateFlow()

    private var controller: MediaController? = null
    private var connecting: ListenableFuture<MediaController>? = null
    private var job: Job? = null

    /** Connects to the playback service (starting it if needed). Call [release] when the screens go away. */
    fun connect() {
        if (job != null) return
        val token = SessionToken(context, ComponentName(context, PlaybackService::class.java))
        val future = MediaController.Builder(context, token).buildAsync()
        connecting = future
        job = scope.launch {
            val connected = future.await()
            controller = connected
            connected.addListener(object : Player.Listener {
                override fun onEvents(player: Player, events: Player.Events) = refresh()
            })
            refresh()
            // The position moves on by itself; redraw it while playing.
            while (isActive) {
                delay(if (connected.isPlaying) 250 else 1_000)
                refresh()
            }
        }
    }

    fun release() {
        job?.cancel()
        job = null
        connecting?.let(MediaController::releaseFuture)
        connecting = null
        controller = null
    }

    private fun refresh() {
        val player = controller ?: return
        val queue = (0 until player.mediaItemCount).mapNotNull { player.getMediaItemAt(it).toQueueItem() }
        val index = player.currentMediaItemIndex.takeIf { queue.isNotEmpty() && it in queue.indices }
        val known = player.duration.takeIf { it > 0 }?.let { it / 1000.0 }
        _state.value = PlayerUi(
            queue = queue,
            index = index,
            playing = player.isPlaying,
            buffering = player.playbackState == Player.STATE_BUFFERING && player.playWhenReady,
            position = player.currentPosition / 1000.0,
            duration = known ?: index?.let { queue[it].duration },
            shuffle = player.shuffleModeEnabled,
            repeat = player.repeatMode.toRepeat(),
            error = player.playerError?.let(::describe),
        )
    }

    private fun describe(error: androidx.media3.common.PlaybackException): String {
        val cause = generateSequence<Throwable>(error) { it.cause }.last()
        return when (error.errorCode) {
            androidx.media3.common.PlaybackException.ERROR_CODE_IO_NETWORK_CONNECTION_FAILED,
            androidx.media3.common.PlaybackException.ERROR_CODE_IO_NETWORK_CONNECTION_TIMEOUT ->
                "Can't reach the server to play this song."
            androidx.media3.common.PlaybackException.ERROR_CODE_IO_BAD_HTTP_STATUS ->
                "The server couldn't give this song (${cause.message})."
            else -> cause.message ?: error.errorCodeName
        }
    }

    private inline fun withPlayer(action: MediaController.() -> Unit) {
        controller?.action()
    }

    /** Replaces the queue and plays from `start`. */
    fun play(items: List<QueueItem>, start: Int) = withPlayer {
        if (items.isEmpty()) return@withPlayer
        setMediaItems(items.map { it.toMediaItem() }, start.coerceIn(items.indices), 0)
        prepare()
        play()
    }

    /** Adds to the end of the queue; starts playing if nothing was queued. */
    fun enqueue(items: List<QueueItem>) = withPlayer {
        val idle = mediaItemCount == 0
        addMediaItems(items.map { it.toMediaItem() })
        if (idle) {
            prepare()
            play()
        }
    }

    fun toggle() = withPlayer {
        when {
            playerError != null || playbackState == Player.STATE_IDLE -> { prepare(); play() }
            playbackState == Player.STATE_ENDED -> { seekToDefaultPosition(0); play() }
            isPlaying || playWhenReady -> pause()
            else -> play()
        }
    }

    fun next() = withPlayer { if (hasNextMediaItem()) { seekToNextMediaItem(); prepareIfFailed() } }

    /** Previous within the first 3 seconds, else back to the start of this song. */
    fun previous() = withPlayer { seekToPrevious(); prepareIfFailed() }

    fun seek(seconds: Double) = withPlayer { seekTo((seconds * 1000).toLong().coerceAtLeast(0)) }

    fun jump(index: Int) = withPlayer {
        seekToDefaultPosition(index)
        prepareIfFailed()
        play()
    }

    fun remove(index: Int) = withPlayer { removeMediaItem(index) }

    fun setShuffle(on: Boolean) = withPlayer { shuffleModeEnabled = on }

    fun cycleRepeat() = withPlayer {
        repeatMode = when (repeatMode.toRepeat()) {
            RepeatMode.Off -> RepeatMode.All
            RepeatMode.All -> RepeatMode.One
            RepeatMode.One -> RepeatMode.Off
        }.toPlayer()
    }

    fun stop() = withPlayer {
        stop()
        clearMediaItems()
    }

    private fun MediaController.prepareIfFailed() {
        if (playerError != null || playbackState == Player.STATE_IDLE) prepare()
    }
}
