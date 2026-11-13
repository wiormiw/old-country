package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Search
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Replay
import androidx.compose.material.icons.filled.Shuffle
import androidx.compose.material.icons.filled.Sync
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.OutlinedIconButton
import androidx.compose.material3.PlainTooltip
import androidx.compose.material3.TooltipAnchorPosition
import androidx.compose.material3.TooltipBox
import androidx.compose.material3.TooltipDefaults
import androidx.compose.material3.rememberTooltipState
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import dev.wiormiw.oldcountry.data.ApiException
import dev.wiormiw.oldcountry.data.Playlist
import dev.wiormiw.oldcountry.data.TimeSpan
import dev.wiormiw.oldcountry.data.importMessage
import dev.wiormiw.oldcountry.data.isLastingProblem
import dev.wiormiw.oldcountry.data.playlistDownloadMessage
import dev.wiormiw.oldcountry.data.timeAgo
import dev.wiormiw.oldcountry.playback.toQueueItem
import kotlinx.coroutines.delay

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun PlaylistScreen(id: String) {
    val env = LocalEnv.current
    var playlist by remember(id) { mutableStateOf<Playlist?>(null) }
    var error by remember(id) { mutableStateOf<ApiException?>(null) }
    var version by remember { mutableIntStateOf(0) }
    val reload: () -> Unit = { version++ }
    LaunchedEffect(id, version) {
        try {
            playlist = env.api.playlist(id)
            error = null
        } catch (e: ApiException) {
            error = e
        }
    }

    // While its downloads run, refresh which songs are on the server every few seconds; stop once
    // nothing has changed for a while.
    var watching by remember { mutableStateOf(false) }
    LaunchedEffect(watching) {
        var unchanged = 0
        while (watching) {
            delay(4_000)
            val fresh = runCatching { env.api.playlist(id) }.getOrNull() ?: continue
            if (fresh.entries.map { it.isAvailableOffline to it.downloadProblem } ==
                playlist?.entries?.map { it.isAvailableOffline to it.downloadProblem }
            ) unchanged++ else unchanged = 0
            playlist = fresh
            if (fresh.entries.all { it.isAvailableOffline || it.youTubeVideoId == null } || unchanged >= 8) watching = false
        }
    }

    var busy by remember { mutableStateOf<String?>(null) }
    var editing by remember { mutableStateOf(false) }
    var confirmDelete by remember { mutableStateOf(false) }
    var filter by rememberSaveable { mutableStateOf("") }
    var onlyMissing by rememberSaveable { mutableStateOf(false) }
    var addTo by remember { mutableStateOf<List<String>?>(null) }

    val p = playlist
    Screen(p?.name ?: "Playlist", back = true, actions = {
        if (p != null) {
            RowMenu(
                listOf(
                    RowAction("Rename") { editing = true },
                    RowAction("Delete") { confirmDelete = true },
                ),
            )
        }
    }) { modifier ->
        val failed = error
        when {
            p == null && failed != null -> ErrorBox(failed, onRetry = reload)
            p == null -> Centered { androidx.compose.material3.CircularProgressIndicator() }
            else -> {
                val songs = p.entries.distinctBy { it.trackId }
                val fromYouTube = songs.count { it.youTubeVideoId != null }
                val downloaded = songs.count { it.youTubeVideoId != null && it.isAvailableOffline }
                val skipped = songs.count { e -> e.downloadProblem?.let(::isLastingProblem) == true }
                val queue = p.entries.map { it.toQueueItem() }
                val search = filter.trim().lowercase()
                val shown = p.entries.withIndex().filter { (_, e) ->
                    (search.isEmpty() || e.title.lowercase().contains(search) || e.artist.lowercase().contains(search)) &&
                        (!onlyMissing || (!e.isAvailableOffline && e.youTubeVideoId != null))
                }
                val filtering = search.isNotEmpty() || onlyMissing

                LazyColumn(modifier.fillMaxSize(), contentPadding = ListPadding) {
                    item {
                        Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            p.description?.let { Text(it) }
                            Text(
                                buildString {
                                    append("${p.entries.size} song${if (p.entries.size == 1) "" else "s"} · ${TimeSpan.display(p.totalDuration)}")
                                    if (fromYouTube > 0) append(" · $downloaded of $fromYouTube downloaded")
                                    p.youTubeSyncedAt?.let { append(" · synced with YouTube ${timeAgo(it)}") }
                                },
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                            // Icons only on a phone; with their names where there's room.
                            val wide = LocalConfiguration.current.screenWidthDp >= 600
                            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                ActionButton("Play all", Icons.Filled.PlayArrow, wide, primary = true, enabled = queue.isNotEmpty()) {
                                    env.player.play(queue, 0)
                                }
                                ActionButton("Shuffle", Icons.Filled.Shuffle, wide, enabled = queue.isNotEmpty()) {
                                    env.player.setShuffle(true)
                                    env.player.play(queue, queue.indices.random())
                                }
                                if (downloaded < fromYouTube) {
                                    ActionButton(
                                        if (watching) "Downloading…" else "Download all", Icons.Filled.Download, wide,
                                        enabled = busy == null, busy = busy == "download" || watching,
                                    ) {
                                        busy = "download"
                                        env.call({ env.api.downloadPlaylist(id, retrySkipped = false) }, onDone = { busy = null }) {
                                            env.notice(playlistDownloadMessage(it))
                                            watching = it.queued + it.alreadyQueued > 0
                                        }
                                    }
                                }
                                if (skipped > 0) {
                                    ActionButton("Retry skipped ($skipped)", Icons.Filled.Replay, wide, enabled = busy == null, busy = busy == "retry", badge = skipped) {
                                        busy = "retry"
                                        env.call({ env.api.downloadPlaylist(id, retrySkipped = true) }, onDone = { busy = null }) {
                                            env.notice(playlistDownloadMessage(it))
                                            watching = it.queued + it.alreadyQueued > 0
                                        }
                                    }
                                }
                                if (p.youTubeListId != null) {
                                    ActionButton(
                                        if (busy == "sync") "Syncing…" else "Sync with YouTube", Icons.Filled.Sync, wide,
                                        enabled = busy == null, busy = busy == "sync",
                                    ) {
                                        busy = "sync"
                                        env.call({ env.api.syncPlaylist(id) }, onDone = { busy = null }) {
                                            env.notice(importMessage(it))
                                            reload()
                                        }
                                    }
                                }
                            }
                            if (p.entries.size > 8) {
                                OutlinedTextField(
                                    filter, { filter = it },
                                    placeholder = { Text("Find in this playlist") },
                                    leadingIcon = { Icon(Icons.Filled.Search, null) },
                                    singleLine = true,
                                    modifier = Modifier.fillMaxWidth(),
                                )
                            }
                            if (fromYouTube > downloaded) {
                                FilterChip(selected = onlyMissing, onClick = { onlyMissing = !onlyMissing }, label = { Text("Not downloaded") })
                            }
                        }
                    }
                    if (p.entries.isEmpty()) {
                        item { EmptyText("No songs yet. Add some from the Library or YouTube.") }
                    }
                    items(shown, key = { (i, e) -> "$i-${e.trackId}" }) { (i, e) ->
                        val actions = buildList {
                            add(RowAction("Add to queue") { env.player.enqueue(listOf(e.toQueueItem())) })
                            add(RowAction("Add to another playlist…") { addTo = listOf(e.trackId) })
                            if (!filtering && i > 0) add(RowAction("Move up") { move(env, id, p, i, i - 1, { playlist = it }, reload) })
                            if (!filtering && i < p.entries.size - 1) add(RowAction("Move down") { move(env, id, p, i, i + 1, { playlist = it }, reload) })
                            add(RowAction("Remove from playlist") {
                                playlist = p.copy(entries = p.entries.filterIndexed { j, _ -> j != i })
                                env.call({ env.api.removeFromPlaylist(id, i) }, onDone = reload)
                            })
                            add(RowAction("Details") { env.nav.navigate(Route.Track(e.trackId)) })
                        }
                        SongRow(
                            title = e.title,
                            artist = e.artist,
                            duration = e.duration,
                            videoId = e.youTubeVideoId,
                            onPlay = { env.player.play(queue, i) },
                            actions = actions,
                            badges = { TrackBadges(e.isAvailableOffline, e.youTubeVideoId, e.downloadProblem) },
                        )
                    }
                }
            }
        }
    }

    if (editing && p != null) {
        PlaylistDialog("Rename playlist", "Save", p.name, p.description.orEmpty(), onDismiss = { editing = false }) { name, description, done ->
            env.call({ env.api.updatePlaylist(id, name, description) }, onDone = done) {
                editing = false
                reload()
            }
        }
    }
    if (confirmDelete && p != null) {
        AlertDialog(
            onDismissRequest = { confirmDelete = false },
            title = { Text("Delete \"${p.name}\"?") },
            text = { Text("The songs stay in the library.") },
            confirmButton = {
                TextButton(onClick = {
                    env.call({ env.api.deletePlaylist(id) }) {
                        confirmDelete = false
                        env.notice("Deleted \"${p.name}\".")
                        env.nav.popBackStack()
                    }
                }) { Text("Delete", color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = { TextButton(onClick = { confirmDelete = false }) { Text("Keep") } },
        )
    }
    AddToPlaylistDialog(addTo) { addTo = null }
}

/** Moves an entry at once on screen, then on the server (reloading if the server says no). */
private fun move(env: AppEnv, id: String, p: Playlist, from: Int, to: Int, show: (Playlist) -> Unit, reload: () -> Unit) {
    show(p.copy(entries = p.entries.toMutableList().apply { add(to, removeAt(from)) }))
    env.call({ env.api.moveInPlaylist(id, from, to) }, onError = reload)
}

/**
 * A playlist action: an icon button with its name as a tooltip (long-press) on a phone, or a
 * button with icon and name when the screen is wide.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ActionButton(
    label: String,
    icon: ImageVector,
    wide: Boolean,
    primary: Boolean = false,
    enabled: Boolean = true,
    busy: Boolean = false,
    badge: Int? = null,
    onClick: () -> Unit,
) {
    val glyph: @Composable () -> Unit = {
        if (busy) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp) else Icon(icon, null, Modifier.size(18.dp))
    }
    if (wide) {
        val content: @Composable RowScope.() -> Unit = {
            glyph()
            Spacer(Modifier.width(8.dp))
            Text(label)
        }
        if (primary) Button(onClick, enabled = enabled, content = content) else OutlinedButton(onClick, enabled = enabled, content = content)
        return
    }
    TooltipBox(
        positionProvider = TooltipDefaults.rememberTooltipPositionProvider(TooltipAnchorPosition.Above),
        tooltip = { PlainTooltip { Text(label) } },
        state = rememberTooltipState(),
    ) {
        val button: @Composable () -> Unit = {
            if (primary) {
                FilledIconButton(onClick, enabled = enabled, modifier = Modifier.semantics { contentDescription = label }) { glyph() }
            } else {
                OutlinedIconButton(onClick, enabled = enabled, modifier = Modifier.semantics { contentDescription = label }) { glyph() }
            }
        }
        if (badge != null) BadgedBox(badge = { androidx.compose.material3.Badge { Text("$badge") } }) { button() } else button()
    }
}
