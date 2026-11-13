package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import dev.wiormiw.oldcountry.data.ApiException
import dev.wiormiw.oldcountry.data.Track
import dev.wiormiw.oldcountry.data.problemLabel
import dev.wiormiw.oldcountry.data.timeAgo
import dev.wiormiw.oldcountry.playback.toQueueItem
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.filter

private const val PAGE_SIZE = 50

/** Every song on the server, searchable, loaded a page at a time as you scroll. */
@Composable
fun LibraryScreen() {
    val env = LocalEnv.current
    var typed by rememberSaveable { mutableStateOf("") }
    var search by rememberSaveable { mutableStateOf("") }
    LaunchedEffect(typed) {
        delay(300)
        search = typed.trim()
    }

    val tracks = remember(search) { mutableStateListOf<Track>() }
    var total by remember(search) { mutableIntStateOf(-1) }
    var loading by remember(search) { mutableStateOf(false) }
    var error by remember(search) { mutableStateOf<ApiException?>(null) }
    var refreshes by remember { mutableIntStateOf(0) }
    var addTo by remember { mutableStateOf<List<String>?>(null) }
    var scanning by remember { mutableStateOf(false) }
    var scanNote by remember { mutableStateOf<String?>(null) }

    suspend fun loadMore() {
        if (loading || (total >= 0 && tracks.size >= total)) return
        loading = true
        try {
            val page = env.api.tracks(tracks.size / PAGE_SIZE + 1, PAGE_SIZE, search.ifBlank { null })
            tracks += page.items.filter { new -> tracks.none { it.id == new.id } }
            total = page.totalCount
            error = null
        } catch (e: ApiException) {
            error = e
        } finally {
            loading = false
        }
    }

    LaunchedEffect(search, refreshes) {
        tracks.clear()
        total = -1
        loadMore()
        scanNote = runCatching { env.api.scanStatus() }.getOrNull()?.lastScan?.let { "Last scan ${timeAgo(it.finishedAt)}: ${it.added} added" }
    }

    val list = rememberLazyListState()
    val nearEnd by remember { derivedStateOf { list.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: 0 } }
    LaunchedEffect(list, search) {
        snapshotFlow { nearEnd }.distinctUntilChanged().filter { it >= tracks.size - 10 && tracks.isNotEmpty() }.collect { loadMore() }
    }

    Screen(
        "Library",
        actions = {
            IconButton(enabled = !scanning, onClick = {
                scanning = true
                env.call({ env.api.scanLibrary() }, onDone = { scanning = false }) {
                    env.notice(if (it.isQueued || it.isRunning) "Scanning the music folder… pull down to refresh when it's done." else "Scan started.")
                }
            }) {
                if (scanning) CircularProgressIndicator(Modifier.padding(12.dp), strokeWidth = 2.dp) else Icon(Icons.Filled.Refresh, "Scan the music folder")
            }
        },
    ) { modifier ->
        Column(modifier.fillMaxSize()) {
            OutlinedTextField(
                value = typed,
                onValueChange = { typed = it },
                placeholder = { Text("Search titles and artists") },
                leadingIcon = { Icon(Icons.Filled.Search, null) },
                singleLine = true,
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
            )
            if (total >= 0) {
                Text(
                    listOfNotNull("$total song${if (total == 1) "" else "s"}", scanNote).joinToString(" · "),
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(horizontal = 20.dp, vertical = 4.dp),
                )
            }
            PullToRefreshBox(
                isRefreshing = loading && tracks.isEmpty() && total >= 0,
                onRefresh = { refreshes++ },
                modifier = Modifier.fillMaxSize(),
            ) {
                val failed = error
                when {
                    tracks.isEmpty() && failed != null -> ErrorBox(failed, onRetry = { refreshes++ })
                    tracks.isEmpty() && (loading || total < 0) -> Centered { CircularProgressIndicator() }
                    tracks.isEmpty() -> EmptyText(
                        if (search.isBlank()) "No songs yet. Put music in the server's music folder and scan, or download from YouTube."
                        else "Nothing matches \"$search\".",
                    )
                    else -> LazyColumn(state = list, contentPadding = ListPadding) {
                        itemsIndexed(tracks, key = { _, t -> t.id }) { i, track ->
                            SongRow(
                                title = track.title,
                                artist = track.artist,
                                duration = track.duration,
                                videoId = track.youTubeVideoId,
                                onPlay = { env.player.play(tracks.map { it.toQueueItem() }, i) },
                                actions = listOf(
                                    RowAction("Add to queue") { env.player.enqueue(listOf(track.toQueueItem())) },
                                    RowAction("Add to playlist…") { addTo = listOf(track.id) },
                                    RowAction("Details") { env.nav.navigate(Route.Track(track.id)) },
                                ),
                                badges = { TrackBadges(track.isAvailableOffline, track.youTubeVideoId, track.downloadProblem) },
                            )
                        }
                        if (loading) item { Centered { CircularProgressIndicator() } }
                    }
                }
            }
        }
    }
    AddToPlaylistDialog(addTo) { addTo = null }
}

/** "Not downloaded" for YouTube songs the server only streams, or why YouTube refused one. */
@Composable
fun TrackBadges(offline: Boolean, videoId: String?, problem: String?) {
    when {
        problem != null -> Badge(problemLabel(problem), MaterialTheme.colorScheme.error)
        !offline && videoId != null -> Badge("Not downloaded")
    }
}
