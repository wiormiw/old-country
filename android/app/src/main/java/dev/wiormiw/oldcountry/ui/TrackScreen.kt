package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import dev.wiormiw.oldcountry.data.TimeSpan
import dev.wiormiw.oldcountry.data.problemLabel
import dev.wiormiw.oldcountry.playback.toQueueItem

/** One song: what the server knows about it, and what to do with it. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun TrackScreen(id: String) {
    val env = LocalEnv.current
    val detail = rememberLoad(id, refreshWhen = { it.touchesTracks || it.touchesPlaylistList }) { env.api.track(id) }
    var addTo by remember { mutableStateOf<List<String>?>(null) }
    var downloading by remember { mutableStateOf(false) }

    Screen("Song", back = true) { modifier ->
        Loaded(detail) { d ->
            val track = d.track
            Column(modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp)) {
                Artwork(track.youTubeVideoId, null, Modifier.padding(horizontal = 40.dp))
                Spacer(Modifier.height(16.dp))
                Text(track.title, style = MaterialTheme.typography.headlineSmall)
                Text(track.artist, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Spacer(Modifier.height(16.dp))
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Button(onClick = { env.player.play(listOf(track.toQueueItem()), 0) }) { Text("Play") }
                    OutlinedButton(onClick = { env.player.enqueue(listOf(track.toQueueItem())) }) { Text("Add to queue") }
                    OutlinedButton(onClick = { addTo = listOf(track.id) }) { Text("Add to playlist") }
                    val videoId = track.youTubeVideoId
                    if (videoId != null && !track.isAvailableOffline) {
                        OutlinedButton(enabled = !downloading, onClick = {
                            downloading = true
                            env.call({ env.api.download(listOf(videoId)) }, onDone = { downloading = false }) {
                                env.notice("Download queued. See Downloads for progress.")
                            }
                        }) { Text("Download to server") }
                    }
                }
                Spacer(Modifier.height(20.dp))
                Facts(
                    "Length" to TimeSpan.display(track.duration),
                    "On the server" to if (track.isAvailableOffline) "Downloaded" else "Streamed from YouTube",
                    "Last download" to track.downloadProblem?.let(::problemLabel),
                    "Format" to track.codec,
                    "Bitrate" to d.bitrateKbps?.let { "$it kbps" },
                    "Sample rate" to d.sampleRateHz?.let { "%.1f kHz".format(it / 1000.0) },
                    "Size" to d.sizeBytes?.let { "%.1f MB".format(it / 1_048_576.0) },
                    "File" to d.fileName,
                )
                if (d.playlists.isNotEmpty()) {
                    Spacer(Modifier.height(20.dp))
                    Text("In playlists", style = MaterialTheme.typography.titleSmall)
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        d.playlists.forEach { p ->
                            AssistChip(onClick = { env.nav.navigate(Route.Playlist(p.id)) }, label = { Text(p.name) })
                        }
                    }
                }
            }
        }
    }
    AddToPlaylistDialog(addTo) { addTo = null }
}

@Composable
private fun Facts(vararg facts: Pair<String, String?>) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        facts.filter { it.second != null }.forEach { (label, value) ->
            Row(Modifier.fillMaxWidth()) {
                Text(label, Modifier.weight(0.4f), color = MaterialTheme.colorScheme.onSurfaceVariant)
                Text(value!!, Modifier.weight(0.6f))
            }
        }
    }
}
