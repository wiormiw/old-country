package dev.wiormiw.oldcountry.data

import java.time.Duration
import java.time.OffsetDateTime

// Wording shared by several screens, the same as the desktop app's.

/** A song's last refused download (`downloadProblem`), in a few words. */
fun problemLabel(reason: String): String = when (reason) {
    "PremiumOnly" -> "Premium only"
    "Private" -> "Private"
    "Unavailable" -> "Removed"
    "AgeRestricted" -> "Age restricted"
    "RegionBlocked" -> "Not in your country"
    else -> "Download failed"
}

/** Refusals that won't change by simply trying again; "Download all" skips these. */
fun isLastingProblem(reason: String): Boolean = reason != "Failed"

/** The same label for a failed download job's error code. */
fun errorCodeLabel(code: String?): String? = when (code) {
    "youtube.premium_only" -> "Premium only"
    "youtube.private" -> "Private"
    "youtube.unavailable" -> "Removed"
    "youtube.age_restricted" -> "Age restricted"
    "youtube.region_blocked" -> "Not in your country"
    "youtube.rate_limited" -> "YouTube asked to slow down"
    "download.gave_up" -> "Gave up after 3 tries"
    else -> null
}

/** What an import or a sync did, in one sentence. */
fun importMessage(report: ImportReport): String {
    val skipped = if (report.skipped > 0) ", ${report.skipped} skipped" else ""
    val added = report.added
    return when {
        report.isNew == false && added == 0 -> "\"${report.name}\" is already up to date with YouTube."
        report.isNew == false && added != null ->
            "Updated \"${report.name}\" from YouTube: $added new song${if (added == 1) "" else "s"} added (${report.entries} in total)$skipped."
        else -> "Imported \"${report.name}\": ${report.entries} songs, ${report.tracksCreated} new to the library$skipped."
    }
}

/** What "Download all" queued, in one sentence. */
fun playlistDownloadMessage(report: PlaylistDownloadReport): String {
    val parts = mutableListOf(
        when (report.queued) {
            0 -> "Nothing new to download"
            1 -> "1 song queued for download"
            else -> "${report.queued} songs queued for download"
        },
    )
    if (report.alreadyQueued > 0) parts += "${report.alreadyQueued} already on the way"
    if (report.skipped > 0) parts += "${report.skipped} skipped (refused by YouTube before; use Retry skipped)"
    return "${parts.joinToString(", ")}. See Downloads for progress."
}

/** "just now", "5 min ago", "3 h ago", "yesterday", "2 days ago". */
fun timeAgo(iso: String, now: OffsetDateTime = OffsetDateTime.now()): String {
    val then = runCatching { OffsetDateTime.parse(iso) }.getOrNull() ?: return iso
    val minutes = Duration.between(then, now).toMinutes().coerceAtLeast(0)
    return when {
        minutes == 0L -> "just now"
        minutes < 60 -> "$minutes min ago"
        minutes < 1_440 -> "${minutes / 60} h ago"
        minutes / 1_440 == 1L -> "yesterday"
        else -> "${minutes / 1_440} days ago"
    }
}
