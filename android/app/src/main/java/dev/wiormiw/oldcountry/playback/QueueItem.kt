package dev.wiormiw.oldcountry.playback

import android.net.Uri
import android.os.Bundle
import androidx.core.net.toUri
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import dev.wiormiw.oldcountry.data.PlaylistEntry
import dev.wiormiw.oldcountry.data.Track
import dev.wiormiw.oldcountry.data.YouTubeResult

/** Where a song's audio comes from on the server. */
sealed interface AudioRef {
    /** Server path, relative to the API base URL. */
    val path: String

    /** Stable key for the on-phone cache, and the queue item's media id. */
    val key: String

    data class Track(val id: String) : AudioRef {
        override val path get() = "/api/tracks/$id/audio"
        override val key get() = "track-$id"
    }

    data class YouTube(val videoId: String) : AudioRef {
        override val path get() = "/api/youtube-music/videos/$videoId/audio"
        override val key get() = "yt-$videoId"
    }

    /** Stands in for the real URL until playback, so a changed server address still plays the queue. */
    val uri: Uri get() = "$SCHEME://audio/$key".toUri()

    companion object {
        const val SCHEME = "oldcountry"

        fun fromKey(key: String): AudioRef? = when {
            key.startsWith("track-") -> Track(key.removePrefix("track-"))
            key.startsWith("yt-") -> YouTube(key.removePrefix("yt-"))
            else -> null
        }

        fun fromUri(uri: Uri): AudioRef? = if (uri.scheme == SCHEME) uri.lastPathSegment?.let(::fromKey) else null
    }
}

/** One song in the play queue. */
data class QueueItem(
    val audio: AudioRef,
    val title: String,
    val artist: String,
    /** Seconds, when known before playing. */
    val duration: Double?,
    /** The YouTube video it came from, for artwork. */
    val videoId: String? = null,
)

private const val EXTRA_VIDEO_ID = "videoId"

/** A library song plays its file on the server, or streams from YouTube when it isn't downloaded. */
private fun libraryAudio(trackId: String, videoId: String?, downloaded: Boolean): AudioRef =
    if (videoId != null && !downloaded) AudioRef.YouTube(videoId) else AudioRef.Track(trackId)

fun Track.toQueueItem() = QueueItem(libraryAudio(id, youTubeVideoId, isAvailableOffline), title, artist, duration, youTubeVideoId)

fun PlaylistEntry.toQueueItem() =
    QueueItem(libraryAudio(trackId, youTubeVideoId, isAvailableOffline), title, artist, duration, youTubeVideoId)

/** A search result plays the server's copy when it has one, else streams from YouTube. */
fun YouTubeResult.toQueueItem() = QueueItem(
    audio = if (trackId != null && isAvailableOffline) AudioRef.Track(trackId) else AudioRef.YouTube(videoId),
    title = title,
    artist = artist ?: "",
    duration = duration,
    videoId = videoId,
)

fun thumbnailUrl(videoId: String) = "https://i.ytimg.com/vi/$videoId/hqdefault.jpg"

fun QueueItem.toMediaItem(): MediaItem = MediaItem.Builder()
    .setMediaId(audio.key)
    .setUri(audio.uri)
    .setCustomCacheKey(audio.key)
    .setMediaMetadata(
        MediaMetadata.Builder()
            .setTitle(title)
            .setArtist(artist)
            .setDurationMs(duration?.let { (it * 1000).toLong() })
            .setArtworkUri(videoId?.let { thumbnailUrl(it).toUri() })
            .setIsPlayable(true)
            .setIsBrowsable(false)
            .setMediaType(MediaMetadata.MEDIA_TYPE_MUSIC)
            .setExtras(Bundle().apply { putString(EXTRA_VIDEO_ID, videoId) })
            .build(),
    )
    .build()

fun MediaItem.toQueueItem(): QueueItem? {
    val audio = AudioRef.fromKey(mediaId) ?: return null
    return QueueItem(
        audio = audio,
        title = mediaMetadata.title?.toString().orEmpty(),
        artist = mediaMetadata.artist?.toString().orEmpty(),
        duration = mediaMetadata.durationMs?.let { it / 1000.0 },
        videoId = mediaMetadata.extras?.getString(EXTRA_VIDEO_ID),
    )
}
