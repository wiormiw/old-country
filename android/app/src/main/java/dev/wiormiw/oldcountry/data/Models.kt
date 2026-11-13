package dev.wiormiw.oldcountry.data

import kotlinx.serialization.KSerializer
import kotlinx.serialization.Serializable
import kotlinx.serialization.descriptors.PrimitiveKind
import kotlinx.serialization.descriptors.PrimitiveSerialDescriptor
import kotlinx.serialization.encoding.Decoder
import kotlinx.serialization.encoding.Encoder

// The server's response shapes (camelCase JSON, .NET TimeSpan durations as "hh:mm:ss").

@Serializable
data class Track(
    val id: String,
    val title: String,
    val artist: String,
    @Serializable(with = TimeSpanSerializer::class) val duration: Double,
    val isAvailableOffline: Boolean,
    val codec: String? = null,
    val youTubeVideoId: String? = null,
    val createdAt: String,
    val downloadProblem: String? = null,
)

@Serializable
data class PlaylistRef(val id: String, val name: String)

@Serializable
data class TrackDetail(
    val track: Track,
    val fileName: String? = null,
    val sizeBytes: Long? = null,
    val bitrateKbps: Int? = null,
    val sampleRateHz: Int? = null,
    val playlists: List<PlaylistRef> = emptyList(),
)

@Serializable
data class Paged<T>(
    val items: List<T>,
    val page: Int,
    val pageSize: Int,
    val totalCount: Int,
    val totalPages: Int,
)

@Serializable
data class ScanReport(
    val trigger: String,
    val filesSeen: Int,
    val added: Int,
    val alreadyKnown: Int,
    val unreadable: Int,
    val missing: Int,
    val startedAt: String,
    val finishedAt: String,
)

@Serializable
data class ScanStatus(val isRunning: Boolean, val isQueued: Boolean, val lastScan: ScanReport? = null)

@Serializable
data class PlaylistSummary(
    val id: String,
    val name: String,
    val description: String? = null,
    val trackCount: Int,
    val createdAt: String,
    val updatedAt: String? = null,
    val youTubeListId: String? = null,
    val youTubeSyncedAt: String? = null,
)

@Serializable
data class PlaylistEntry(
    val position: Int,
    val trackId: String,
    val title: String,
    val artist: String,
    @Serializable(with = TimeSpanSerializer::class) val duration: Double,
    val isAvailableOffline: Boolean,
    val youTubeVideoId: String? = null,
    val addedAt: String,
    val downloadProblem: String? = null,
)

@Serializable
data class Playlist(
    val id: String,
    val name: String,
    val description: String? = null,
    val entries: List<PlaylistEntry>,
    @Serializable(with = TimeSpanSerializer::class) val totalDuration: Double,
    val createdAt: String,
    val updatedAt: String? = null,
    val youTubeListId: String? = null,
    val youTubeSyncedAt: String? = null,
)

@Serializable
data class YouTubeResult(
    val videoId: String,
    val title: String,
    val artist: String? = null,
    @Serializable(with = TimeSpanSerializer::class) val duration: Double? = null,
    val thumbnailUrl: String? = null,
    val trackId: String? = null,
    val isAvailableOffline: Boolean,
)

@Serializable
enum class DownloadState { Queued, Running, Completed, Failed }

@Serializable
data class DownloadStatus(
    val jobId: String,
    val videoId: String,
    val title: String? = null,
    val state: DownloadState,
    val trackId: String? = null,
    val errorCode: String? = null,
    val error: String? = null,
    val attempts: Int = 0,
    val queuedAt: String,
    val finishedAt: String? = null,
)

@Serializable
data class Downloads(
    val jobs: List<DownloadStatus> = emptyList(),
    val pausedUntil: String? = null,
    val pauseReason: String? = null,
)

@Serializable
data class PlaylistDownloadReport(
    val queued: Int,
    val alreadyDownloaded: Int,
    val alreadyQueued: Int,
    val skipped: Int,
    val notOnYouTube: Int,
)

@Serializable
data class ImportReport(
    val playlistId: String,
    val name: String,
    val isNew: Boolean? = null,
    val added: Int? = null,
    val entries: Int,
    val tracksCreated: Int,
    val tracksExisting: Int,
    val skipped: Int,
)

/** RFC 9457 problem details, as the server reports every error. */
@Serializable
data class ProblemDetails(
    val title: String? = null,
    val status: Int? = null,
    val detail: String? = null,
    val code: String? = null,
    val errors: Map<String, List<String>> = emptyMap(),
)

/** .NET TimeSpan text (`[-][d.]hh:mm:ss[.fffffff]`) as seconds. */
object TimeSpan {
    fun parse(text: String): Double? {
        val negative = text.startsWith('-')
        val parts = text.removePrefix("-").split(':')
        if (parts.size != 3) return null
        val first = parts[0]
        val dot = first.indexOf('.')
        val days = if (dot >= 0) first.substring(0, dot).toDoubleOrNull() ?: return null else 0.0
        val hours = (if (dot >= 0) first.substring(dot + 1) else first).toDoubleOrNull() ?: return null
        val minutes = parts[1].toDoubleOrNull() ?: return null
        val seconds = parts[2].toDoubleOrNull() ?: return null
        val total = days * 86_400 + hours * 3_600 + minutes * 60 + seconds
        return if (negative) -total else total
    }

    fun format(seconds: Double): String {
        val total = Math.round(seconds)
        val sign = if (total < 0) "-" else ""
        val abs = kotlin.math.abs(total)
        val days = abs / 86_400
        val rest = abs % 86_400
        val hms = "%02d:%02d:%02d".format(rest / 3_600, rest % 3_600 / 60, rest % 60)
        return if (days > 0) "$sign$days.$hms" else "$sign$hms"
    }

    /** For people: `3:35`, `1:02:03`. */
    fun display(seconds: Double?): String {
        if (seconds == null || seconds.isNaN() || seconds < 0) return "–:––"
        val total = seconds.toLong()
        val h = total / 3_600
        val m = total % 3_600 / 60
        val s = total % 60
        return if (h > 0) "%d:%02d:%02d".format(h, m, s) else "%d:%02d".format(m, s)
    }
}

object TimeSpanSerializer : KSerializer<Double> {
    override val descriptor = PrimitiveSerialDescriptor("TimeSpan", PrimitiveKind.STRING)

    override fun deserialize(decoder: Decoder): Double {
        val text = decoder.decodeString()
        return TimeSpan.parse(text) ?: throw kotlinx.serialization.SerializationException("not a TimeSpan: $text")
    }

    override fun serialize(encoder: Encoder, value: Double) = encoder.encodeString(TimeSpan.format(value))
}
