package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.PlaylistAdd
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.NavHostController
import dev.wiormiw.oldcountry.data.ApiClient
import dev.wiormiw.oldcountry.data.ApiException
import dev.wiormiw.oldcountry.data.SettingsStore
import dev.wiormiw.oldcountry.live.LibraryChanges
import dev.wiormiw.oldcountry.live.LiveUpdates
import dev.wiormiw.oldcountry.playback.PlayerConnection
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch

/** What every screen needs, handed down by [AppScaffold]. */
class AppEnv(
    val api: ApiClient,
    val settings: SettingsStore,
    val player: PlayerConnection,
    val live: LiveUpdates,
    val nav: NavHostController,
    val snackbar: SnackbarHostState,
    val scope: CoroutineScope,
) {
    /** Opens Settings over the current screen (once, however often it's asked). */
    fun openSettings() = nav.navigate(Route.Settings) { launchSingleTop = true }

    /**
     * Switches to a tab, keeping each tab's place. Settings is a detour, not part of any tab: it's
     * left first, or it would be saved with the tab it was opened from and come back with it.
     */
    fun openTab(route: Any) {
        nav.popBackStack(Route.Settings, inclusive = true)
        nav.navigate(route) {
            popUpTo(nav.graph.findStartDestination().id) { saveState = true }
            launchSingleTop = true
            restoreState = true
        }
    }

    /** Shows a short message at the bottom. */
    fun notice(message: String) {
        scope.launch {
            snackbar.currentSnackbarData?.dismiss()
            snackbar.showSnackbar(message, withDismissAction = true)
        }
    }

    /** Runs a server call, showing its error (if any) as a notice. */
    fun <T> call(block: suspend () -> T, onDone: () -> Unit = {}, onError: () -> Unit = {}, onSuccess: (T) -> Unit = {}) {
        scope.launch {
            try {
                onSuccess(block())
            } catch (e: ApiException) {
                notice(e.message)
                onError()
            } finally {
                onDone()
            }
        }
    }
}

val LocalEnv = staticCompositionLocalOf<AppEnv> { error("no AppEnv") }

/** A server answer being fetched, fetched, or failed. */
sealed interface Load<out T> {
    data object Loading : Load<Nothing>
    data class Ready<T>(val value: T) : Load<T>
    data class Failed(val error: ApiException) : Load<Nothing>
}

class Loader<T>(val state: State<Load<T>>, private val version: MutableState<Int>) {
    fun reload() {
        version.value++
    }
}

/**
 * Fetches `fetch` when `keys` change, on [Loader.reload], and when the server says something it
 * shows changed (`refreshWhen`), keeping the last answer on screen while it reloads.
 */
@Composable
fun <T> rememberLoad(
    vararg keys: Any?,
    refreshWhen: (LibraryChanges) -> Boolean = { false },
    fetch: suspend () -> T,
): Loader<T> {
    val version = remember { mutableStateOf(0) }
    val live = LocalEnv.current.live
    LaunchedEffect(*keys) {
        live.changes.filter(refreshWhen).collect { version.value++ }
    }
    val state = remember(*keys) { mutableStateOf<Load<T>>(Load.Loading) }
    LaunchedEffect(*keys, version.value) {
        state.value = try {
            Load.Ready(fetch())
        } catch (e: ApiException) {
            Load.Failed(e)
        }
    }
    return remember(state, version) { Loader(state, version) }
}

/** Shows `content` once loaded, else a spinner or the error with a Retry button. */
@Composable
fun <T> Loaded(loader: Loader<T>, content: @Composable (T) -> Unit) {
    when (val load = loader.state.value) {
        Load.Loading -> Centered { CircularProgressIndicator() }
        is Load.Failed -> ErrorBox(load.error, onRetry = loader::reload)
        is Load.Ready -> content(load.value)
    }
}

@Composable
fun Centered(content: @Composable () -> Unit) {
    Box(Modifier.fillMaxSize().padding(24.dp), contentAlignment = Alignment.Center) { content() }
}

@Composable
fun ErrorBox(error: ApiException, onRetry: () -> Unit) {
    val env = LocalEnv.current
    Centered {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text(error.message, textAlign = TextAlign.Center, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = onRetry) { Text("Try again") }
                if (error.code.startsWith("server.")) {
                    TextButton(onClick = env::openSettings) { Text("Settings") }
                }
            }
        }
    }
}

@Composable
fun EmptyText(text: String) {
    Centered { Text(text, textAlign = TextAlign.Center, color = MaterialTheme.colorScheme.onSurfaceVariant) }
}

/** A small coloured label: "Not downloaded", "Premium only"... */
@Composable
fun Badge(text: String, color: Color = MaterialTheme.colorScheme.onSurfaceVariant) {
    Text(
        text,
        style = MaterialTheme.typography.labelSmall,
        color = color,
        maxLines = 1,
        modifier = Modifier
            .border(1.dp, color.copy(alpha = 0.45f), RoundedCornerShape(50))
            .padding(horizontal = 8.dp, vertical = 2.dp),
    )
}

/** Picks a playlist to add songs to. Shown while `trackIds` isn't null. */
@Composable
fun AddToPlaylistDialog(trackIds: List<String>?, onDismiss: () -> Unit) {
    if (trackIds == null) return
    val env = LocalEnv.current
    val playlists = rememberLoad(trackIds) { env.api.playlists() }
    var adding by remember { mutableStateOf(false) }
    AlertDialog(
        onDismissRequest = onDismiss,
        icon = { Icon(Icons.AutoMirrored.Filled.PlaylistAdd, null) },
        title = { Text("Add to playlist") },
        text = {
            Box(Modifier.heightIn(min = 80.dp, max = 400.dp)) {
                Loaded(playlists) { list ->
                    if (list.isEmpty()) {
                        Text("No playlists yet. Make one in Playlists.")
                    } else {
                        LazyColumn {
                            items(list, key = { it.id }) { playlist ->
                                ListItem(
                                    headlineContent = { Text(playlist.name, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                                    supportingContent = { Text("${playlist.trackCount} songs") },
                                    modifier = Modifier.fillMaxWidth().clickableUnlessBusy(adding) {
                                        adding = true
                                        env.call({ env.api.addToPlaylist(playlist.id, trackIds) }, onDone = { adding = false }) {
                                            env.notice("Added to \"${playlist.name}\".")
                                            onDismiss()
                                        }
                                    },
                                )
                            }
                        }
                    }
                }
            }
        },
        confirmButton = {},
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}

fun Modifier.clickableUnlessBusy(busy: Boolean, onClick: () -> Unit): Modifier =
    clickable(enabled = !busy, onClick = onClick)

val ListPadding = PaddingValues(bottom = 16.dp)
