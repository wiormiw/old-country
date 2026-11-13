package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Sync
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import dev.wiormiw.oldcountry.data.timeAgo

@Composable
fun PlaylistsScreen() {
    val env = LocalEnv.current
    val playlists = rememberLoad { env.api.playlists() }
    var creating by remember { mutableStateOf(false) }

    Screen("Playlists", floating = {
        ExtendedFloatingActionButton(onClick = { creating = true }, icon = { Icon(Icons.Filled.Add, null) }, text = { Text("New playlist") })
    }) { modifier ->
        PullToRefreshBox(isRefreshing = false, onRefresh = playlists::reload, modifier = modifier.fillMaxSize()) {
            Loaded(playlists) { list ->
                if (list.isEmpty()) {
                    EmptyText("No playlists yet. Make one, or import one from YouTube Music.")
                } else {
                    LazyColumn(contentPadding = ListPadding) {
                        items(list, key = { it.id }) { p ->
                            ListItem(
                                modifier = Modifier.clickable { env.nav.navigate(Route.Playlist(p.id)) },
                                headlineContent = { Text(p.name, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                                supportingContent = {
                                    val synced = p.youTubeSyncedAt?.let { " · synced ${timeAgo(it)}" }.orEmpty()
                                    Text(
                                        "${p.trackCount} song${if (p.trackCount == 1) "" else "s"}$synced" +
                                            (p.description?.let { "\n$it" } ?: ""),
                                        maxLines = 2,
                                        overflow = TextOverflow.Ellipsis,
                                    )
                                },
                                trailingContent = {
                                    if (p.youTubeListId != null) {
                                        Icon(Icons.Filled.Sync, "Kept in sync with YouTube Music", tint = MaterialTheme.colorScheme.primary)
                                    }
                                },
                            )
                        }
                    }
                }
            }
        }
    }

    if (creating) {
        PlaylistDialog(
            title = "New playlist",
            confirm = "Create",
            initialName = "",
            initialDescription = "",
            onDismiss = { creating = false },
        ) { name, description, done ->
            env.call({ env.api.createPlaylist(name, description) }, onDone = done) {
                creating = false
                env.nav.navigate(Route.Playlist(it.id))
            }
        }
    }
}

/** Name and description of a new or existing playlist. `onSave` gets a callback to call when the server answered. */
@Composable
fun PlaylistDialog(
    title: String,
    confirm: String,
    initialName: String,
    initialDescription: String,
    onDismiss: () -> Unit,
    onSave: (name: String, description: String?, done: () -> Unit) -> Unit,
) {
    var name by remember { mutableStateOf(initialName) }
    var description by remember { mutableStateOf(initialDescription) }
    var saving by remember { mutableStateOf(false) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            androidx.compose.foundation.layout.Column {
                OutlinedTextField(name, { name = it }, label = { Text("Name") }, singleLine = true)
                OutlinedTextField(description, { description = it }, label = { Text("Description (optional)") })
            }
        },
        confirmButton = {
            TextButton(enabled = name.isNotBlank() && !saving, onClick = {
                saving = true
                onSave(name.trim(), description.trim().ifEmpty { null }) { saving = false }
            }) { Text(confirm) }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}
