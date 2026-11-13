package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import dev.wiormiw.oldcountry.data.DownloadState

import dev.wiormiw.oldcountry.data.errorCodeLabel
import dev.wiormiw.oldcountry.data.timeAgo
import kotlinx.coroutines.delay
import dev.wiormiw.oldcountry.live.LiveStatus
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue

/** The server's YouTube downloads, refreshed every few seconds while any are waiting or running. */
@Composable
fun DownloadsScreen() {
    val env = LocalEnv.current
    val downloads = rememberLoad(refreshWhen = { it.touchesDownloads }) { env.api.downloads() }
    val active = (downloads.state.value as? Load.Ready)?.value?.jobs?.any { it.state == DownloadState.Queued || it.state == DownloadState.Running } == true
    // The server says when downloads change; polling is only the fallback while that's down.
    val live by env.live.status.collectAsState()
    LaunchedEffect(active, live) {
        while (active && live != LiveStatus.Live) {
            delay(3_000)
            downloads.reload()
        }
    }

    Screen("Downloads", actions = {
        IconButton(onClick = downloads::reload) { Icon(Icons.Filled.Refresh, "Refresh") }
    }) { modifier ->
        Loaded(downloads) { d ->
            LazyColumn(modifier.fillMaxSize(), contentPadding = ListPadding) {
                d.pausedUntil?.let { until ->
                    item {
                        Card(
                            Modifier.fillMaxWidth().padding(16.dp),
                            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer),
                        ) {
                            Text(
                                "Downloads are paused until ${until.substringAfter('T').take(5)} (UTC)" +
                                    (d.pauseReason?.let { ": $it" } ?: "."),
                                Modifier.padding(16.dp),
                            )
                        }
                    }
                }
                if (d.jobs.isEmpty()) item { EmptyText("No downloads yet. Download songs from YouTube, or a whole playlist.") }
                items(d.jobs, key = { it.jobId }) { job ->
                    val (label, color) = when (job.state) {
                        DownloadState.Queued -> "Waiting" to MaterialTheme.colorScheme.onSurfaceVariant
                        DownloadState.Running -> "Downloading" to MaterialTheme.colorScheme.primary
                        DownloadState.Completed -> "Done" to MaterialTheme.colorScheme.tertiary
                        DownloadState.Failed -> (errorCodeLabel(job.errorCode) ?: "Failed") to MaterialTheme.colorScheme.error
                    }
                    ListItem(
                        modifier = job.trackId?.let { id -> Modifier.clickable { env.nav.navigate(Route.Track(id)) } } ?: Modifier,
                        leadingContent = { Artwork(job.videoId, 44.dp) },
                        headlineContent = { Text(job.title ?: job.videoId, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                        supportingContent = {
                            Text(
                                listOfNotNull(
                                    timeAgo(job.finishedAt ?: job.queuedAt),
                                    job.attempts.takeIf { it > 1 }?.let { "$it tries" },
                                    job.error.takeIf { job.state == DownloadState.Failed },
                                ).joinToString(" · "),
                                maxLines = 2,
                                overflow = TextOverflow.Ellipsis,
                            )
                        },
                        trailingContent = { Badge(label, color) },
                    )
                }
            }
        }
    }
}
