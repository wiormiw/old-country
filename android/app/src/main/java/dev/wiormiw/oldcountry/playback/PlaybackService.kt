package dev.wiormiw.oldcountry.playback

import android.app.PendingIntent
import android.content.Intent
import androidx.annotation.OptIn
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.session.MediaSession
import androidx.media3.session.MediaSessionService
import com.google.common.util.concurrent.Futures
import com.google.common.util.concurrent.ListenableFuture
import dev.wiormiw.oldcountry.MainActivity
import dev.wiormiw.oldcountry.app
import dev.wiormiw.oldcountry.data.RepeatMode
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import kotlinx.coroutines.runInterruptible
import android.util.Log
import androidx.media3.datasource.cache.CacheWriter

/**
 * Plays in the background: owns the player, and the media session that the notification, the lock
 * screen, Bluetooth and Android Auto/Wear controls talk to. The app's screens talk to it too,
 * through a MediaController (see PlayerConnection).
 */
@OptIn(UnstableApi::class)
class PlaybackService : MediaSessionService() {
    private var session: MediaSession? = null
    private val scope = MainScope()
    private var prefetch: Prefetch? = null

    private class Prefetch(val key: String, val job: Job, val writer: CacheWriter) {
        fun stop() {
            writer.cancel()
            job.cancel()
        }
    }

    /** Unplayable songs in a row skipped before giving up (one bad video shouldn't stop a playlist). */
    private var failures = 0

    override fun onCreate() {
        super.onCreate()
        val app = app
        val player = ExoPlayer.Builder(this)
            .setMediaSourceFactory(DefaultMediaSourceFactory(app.audioCache.dataSourceFactory))
            .setAudioAttributes(
                AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MUSIC).build(),
                /* handleAudioFocus = */ true,
            )
            .setHandleAudioBecomingNoisy(true) // headphones out: pause
            .setWakeMode(C.WAKE_MODE_NETWORK)
            .build()

        val settings = app.settings.settings.value
        player.shuffleModeEnabled = settings.shuffle
        player.repeatMode = settings.repeat.toPlayer()
        player.addListener(object : Player.Listener {
            override fun onShuffleModeEnabledChanged(on: Boolean) {
                scope.launch { app.settings.setShuffle(on) }
            }

            override fun onRepeatModeChanged(mode: Int) {
                scope.launch { app.settings.setRepeat(mode.toRepeat()) }
            }

            override fun onMediaItemTransition(item: MediaItem?, reason: Int) = prefetchNext(player)
            override fun onTimelineChanged(timeline: androidx.media3.common.Timeline, reason: Int) = prefetchNext(player)

            override fun onPlaybackStateChanged(state: Int) {
                if (state == Player.STATE_READY) failures = 0
            }

            override fun onPlayerError(error: PlaybackException) {
                Log.w(TAG, "couldn't play ${player.currentMediaItem?.mediaId}", error)
                failures++
                if (failures < MAX_SKIPS && player.hasNextMediaItem()) {
                    player.seekToNextMediaItem()
                    player.prepare()
                    player.play()
                }
            }
        })

        session = MediaSession.Builder(this, player)
            .setSessionActivity(
                PendingIntent.getActivity(
                    this, 0, Intent(this, MainActivity::class.java),
                    PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
                ),
            )
            .setCallback(object : MediaSession.Callback {
                // Items from controllers arrive without their URL (Media3 strips it); rebuild them.
                override fun onAddMediaItems(
                    session: MediaSession,
                    controller: MediaSession.ControllerInfo,
                    items: List<MediaItem>,
                ): ListenableFuture<List<MediaItem>> =
                    Futures.immediateFuture(items.map { it.toQueueItem()?.toMediaItem() ?: it })
            })
            .build()
    }

    /** Fetches the song that plays next into the cache, so it starts at once (YouTube songs take seconds). */
    private fun prefetchNext(player: Player) {
        val next = player.nextMediaItemIndex.takeIf { it != C.INDEX_UNSET }
            ?.let { player.getMediaItemAt(it).toQueueItem()?.audio }
        if (next != null && next.key == prefetch?.key) return
        prefetch?.stop()
        prefetch = null
        if (next == null || app.audioCache.isCached(next)) return
        val writer = app.audioCache.writer(next)
        val job = scope.launch(Dispatchers.IO) {
            runCatching { runInterruptible { writer.cache() } }
                .onFailure { Log.d(TAG, "prefetch of ${next.key} stopped: ${it.message}") }
        }
        prefetch = Prefetch(next.key, job, writer)
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaSession? = session

    override fun onTaskRemoved(rootIntent: Intent?) {
        // Closed from Recents (its preview swiped away): the music stops and the notification goes,
        // like closing the desktop window. Leaving the app any other way keeps it playing.
        pauseAllPlayersAndStopSelf()
    }

    override fun onDestroy() {
        prefetch?.stop()
        scope.cancel()
        session?.run {
            player.release()
            release()
        }
        session = null
        super.onDestroy()
    }

    companion object {
        private const val TAG = "PlaybackService"
        private const val MAX_SKIPS = 3
    }
}

fun RepeatMode.toPlayer(): Int = when (this) {
    RepeatMode.Off -> Player.REPEAT_MODE_OFF
    RepeatMode.All -> Player.REPEAT_MODE_ALL
    RepeatMode.One -> Player.REPEAT_MODE_ONE
}

fun Int.toRepeat(): RepeatMode = when (this) {
    Player.REPEAT_MODE_ALL -> RepeatMode.All
    Player.REPEAT_MODE_ONE -> RepeatMode.One
    else -> RepeatMode.Off
}
