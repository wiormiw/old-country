package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import dev.wiormiw.oldcountry.data.ApiClient
import dev.wiormiw.oldcountry.data.ApiException
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import dev.wiormiw.oldcountry.data.YouTubeResult
import dev.wiormiw.oldcountry.data.importMessage
import dev.wiormiw.oldcountry.playback.toQueueItem
import kotlinx.coroutines.launch

private const val RESULTS = 25

/** The last search, kept while you look at other tabs. */
class YouTubeSearch : ViewModel() {
    var searching by mutableStateOf(false)
    var results by mutableStateOf<List<YouTubeResult>?>(null)
    var error by mutableStateOf<ApiException?>(null)

    /** Downloads asked for here: video id -> "sending" or "queued". */
    val downloads = mutableStateMapOf<String, String>()

    fun search(api: ApiClient, text: String) {
        if (text.isEmpty() || searching) return
        searching = true
        error = null
        viewModelScope.launch {
            try {
                results = api.youTubeSearch(text, RESULTS)
            } catch (e: ApiException) {
                error = e
            } finally {
                searching = false
            }
        }
    }
}

/** Search YouTube Music, play or download what you find, and import playlists. */
@Composable
fun YouTubeScreen(model: YouTubeSearch = viewModel()) {
    val env = LocalEnv.current
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    var query by rememberSaveable { mutableStateOf("") }
    val searching = model.searching
    val results = model.results
    val error = model.error
    val downloads = model.downloads
    // Only songs already in the library (with a track id) can go in a playlist.
    var addTo by remember { mutableStateOf<List<String>?>(null) }

    fun search() {
        keyboard?.hide()
        focus.clearFocus()
        model.search(env.api, query.trim())
    }

    Screen("YouTube Music") { modifier ->
        LazyColumn(modifier.fillMaxSize(), contentPadding = ListPadding) {
            item {
                Row(Modifier.padding(horizontal = 16.dp, vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                    OutlinedTextField(
                        query, { query = it },
                        placeholder = { Text("Search songs, artists…") },
                        leadingIcon = { Icon(Icons.Filled.Search, null) },
                        singleLine = true,
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                        keyboardActions = KeyboardActions(onSearch = { search() }),
                        modifier = Modifier.weight(1f),
                    )
                }
            }
            val failed = error
            when {
                searching -> item { Centered { CircularProgressIndicator() } }
                failed != null -> item { ErrorBox(failed, onRetry = ::search) }
                results?.isEmpty() == true -> item { EmptyText("Nothing found.") }
                results == null -> item { ImportCard() }
            }
            val list = results.orEmpty()
            if (!searching) itemsIndexed(list, key = { _, r -> r.videoId }) { i, r ->
                val state = downloads[r.videoId]
                SongRow(
                    title = r.title,
                    artist = r.artist.orEmpty(),
                    duration = r.duration,
                    videoId = r.videoId,
                    onPlay = { env.player.play(list.map { it.toQueueItem() }, i) },
                    actions = buildList {
                        add(RowAction("Add to queue") { env.player.enqueue(listOf(r.toQueueItem())) })
                        if (!r.isAvailableOffline && state == null) {
                            add(RowAction("Download to server") {
                                downloads[r.videoId] = "sending"
                                env.call({ env.api.download(listOf(r.videoId)) }, onError = { downloads.remove(r.videoId) }) {
                                    downloads[r.videoId] = "queued"
                                    env.notice("Download queued. See Downloads for progress.")
                                }
                            })
                        }
                        r.trackId?.let { id ->
                            add(RowAction("Add to playlist…") { addTo = listOf(id) })
                            add(RowAction("Details") { env.nav.navigate(Route.Track(id)) })
                        }
                    },
                    badges = {
                        when {
                            r.isAvailableOffline -> Badge("Downloaded", MaterialTheme.colorScheme.tertiary)
                            state == "queued" -> Badge("Queued")
                            r.trackId != null -> Badge("In library")
                        }
                    },
                )
            }
            if (results != null && !searching) item { ImportCard() }
        }
    }
    AddToPlaylistDialog(addTo) { addTo = null }
}

/** Imports a YouTube Music playlist (or your liked songs) as a playlist here, kept in sync after. */
@Composable
private fun ImportCard() {
    val env = LocalEnv.current
    var source by rememberSaveable { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    Card(Modifier.fillMaxWidth().padding(16.dp)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("Import a playlist", style = MaterialTheme.typography.titleMedium)
            Text(
                "Paste a YouTube Music playlist link, or type \"liked\" for your liked songs (the server needs your YouTube cookies for that). It stays in sync on its own.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            OutlinedTextField(
                source, { source = it },
                placeholder = { Text("https://music.youtube.com/playlist?list=… or liked") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            Button(enabled = source.isNotBlank() && !busy, onClick = {
                busy = true
                env.call({ env.api.import(source.trim()) }, onDone = { busy = false }) {
                    env.notice(importMessage(it))
                    source = ""
                    env.nav.navigate(Route.Playlist(it.playlistId))
                }
            }) { Text(if (busy) "Importing… (a big playlist takes a minute)" else "Import") }
        }
    }
}
