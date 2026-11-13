package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.QueueMusic
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.LibraryMusic
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedCard
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import dev.wiormiw.oldcountry.data.ApiException
import dev.wiormiw.oldcountry.data.timeAgo
import dev.wiormiw.oldcountry.live.LiveStatus
import java.time.LocalTime

/** How many playlists Home offers to jump back into. */
private const val RECENT_PLAYLISTS = 8

/** "Good evening" and friends, by the phone's clock. */
private fun greeting(hour: Int = LocalTime.now().hour) = when (hour) {
    in 5..11 -> "Good morning"
    in 12..17 -> "Good afternoon"
    in 18..22 -> "Good evening"
    else -> "Up late"
}

/** The first screen: a welcome, what's playing, where the server stands, and the way to everything. */
@Composable
fun HomeScreen() {
    val env = LocalEnv.current
    val settings by env.settings.settings.collectAsState()
    val player by env.player.state.collectAsState()
    // Each reloads when the server address changes.
    val server = rememberLoad(settings.serverUrl) { env.api.checkServer(settings.serverUrl) }
    val songs = rememberLoad(settings.serverUrl, refreshWhen = { it.touchesTracks }) { env.api.tracks(1, 1, null).totalCount }
    val playlists = rememberLoad(settings.serverUrl, refreshWhen = { it.touchesPlaylistList }) {
        env.api.playlists().sortedByDescending { it.updatedAt ?: it.createdAt }.take(RECENT_PLAYLISTS)
    }

    Screen("old-country", actions = {
        IconButton(onClick = { env.nav.navigate(Route.Settings) }) { Icon(Icons.Filled.Settings, "Settings") }
    }) { modifier ->
        LazyColumn(modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            item {
                Column(Modifier.padding(horizontal = 20.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Text(
                        "WELCOME HOME",
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.primary,
                        letterSpacing = MaterialTheme.typography.labelMedium.letterSpacing * 2,
                    )
                    Text(greeting(), style = MaterialTheme.typography.displaySmall)
                    Text(
                        (songs.state.value as? Load.Ready)?.value?.let { "$it songs in your library." }
                            ?: "Your library and YouTube Music, in one place.",
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    ServerLine(server.state.value)
                    Spacer(Modifier.height(4.dp))
                    ThemeToggle(Modifier.fillMaxWidth())
                }
            }

            player.current?.let { current ->
                item {
                    Card(
                        Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer),
                    ) {
                        Row(Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically) {
                            Artwork(current.videoId, 72.dp)
                            Spacer(Modifier.width(14.dp))
                            Column(Modifier.weight(1f)) {
                                Text(
                                    if (player.playing) "Now playing" else "Paused",
                                    style = MaterialTheme.typography.labelMedium,
                                    color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.75f),
                                )
                                Text(current.title, style = MaterialTheme.typography.titleLarge, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                Text(current.artist, maxLines = 1, overflow = TextOverflow.Ellipsis, style = MaterialTheme.typography.bodySmall)
                            }
                            IconButton(onClick = env.player::toggle) {
                                Icon(if (player.playing) Icons.Filled.Pause else Icons.Filled.PlayArrow, if (player.playing) "Pause" else "Play")
                            }
                        }
                    }
                }
            }

            item {
                Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        Shortcut("Library", Icons.Filled.LibraryMusic, Modifier.weight(1f)) { env.nav.navigate(Route.Library) }
                        Shortcut("Playlists", Icons.AutoMirrored.Filled.QueueMusic, Modifier.weight(1f)) { env.nav.navigate(Route.Playlists) }
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        Shortcut("YouTube", Icons.Filled.Search, Modifier.weight(1f)) { env.nav.navigate(Route.YouTube) }
                        Shortcut("Downloads", Icons.Filled.Download, Modifier.weight(1f)) { env.nav.navigate(Route.Downloads) }
                    }
                }
            }

            val recent = (playlists.state.value as? Load.Ready)?.value.orEmpty()
            if (recent.isNotEmpty()) {
                item {
                    Text(
                        "Jump back in",
                        style = MaterialTheme.typography.titleLarge,
                        modifier = Modifier.padding(start = 20.dp, top = 16.dp, bottom = 8.dp),
                    )
                    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        items(recent, key = { it.id }) { p ->
                            OutlinedCard(
                                onClick = { env.nav.navigate(Route.Playlist(p.id)) },
                                modifier = Modifier.width(170.dp),
                            ) {
                                Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                    Text(p.name, fontWeight = FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                    Text(
                                        "${p.trackCount} songs" + (p.updatedAt?.let { " · ${timeAgo(it)}" } ?: ""),
                                        style = MaterialTheme.typography.bodySmall,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                                        maxLines = 1,
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

/** Connected, checking, or why not, with the way to Settings. */
@Composable
private fun ServerLine(state: Load<Unit>) {
    val env = LocalEnv.current
    val settings by env.settings.settings.collectAsState()
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        when (state) {
            Load.Loading -> Text("Checking the server…", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            is Load.Ready -> {
                val live by env.live.status.collectAsState()
                Dot(MaterialTheme.colorScheme.tertiary)
                Text(
                    "Connected to ${settings.serverUrl}" + if (live == LiveStatus.Live) " · live" else "",
                    style = MaterialTheme.typography.bodySmall,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            is Load.Failed -> {
                Dot(MaterialTheme.colorScheme.error)
                Text(state.error.shortReason(), style = MaterialTheme.typography.bodySmall, modifier = Modifier.weight(1f, fill = false))
                Button(onClick = { env.nav.navigate(Route.Settings) }, contentPadding = PaddingValues(horizontal = 12.dp)) { Text("Settings") }
            }
        }
    }
}

private fun ApiException.shortReason() = if (code == "server.not_configured") "Set up your server to start." else message

@Composable
private fun Dot(color: Color) = Box(Modifier.size(8.dp).clip(CircleShape).background(color))

@Composable
private fun Shortcut(label: String, icon: ImageVector, modifier: Modifier, onClick: () -> Unit) {
    OutlinedCard(onClick = onClick, modifier = modifier) {
        Row(Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(
                Modifier.size(34.dp).clip(RoundedCornerShape(10.dp)).background(MaterialTheme.colorScheme.primary.copy(alpha = 0.14f)),
                contentAlignment = Alignment.Center,
            ) { Icon(icon, null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(20.dp)) }
            Spacer(Modifier.width(12.dp))
            Text(label, fontWeight = FontWeight.SemiBold)
        }
    }
}
