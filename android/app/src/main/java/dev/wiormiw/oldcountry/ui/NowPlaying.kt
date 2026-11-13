package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.MusicNote
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Repeat
import androidx.compose.material.icons.filled.RepeatOne
import androidx.compose.material.icons.filled.Shuffle
import androidx.compose.material.icons.filled.SkipNext
import androidx.compose.material.icons.filled.SkipPrevious
import androidx.compose.material.icons.filled.GraphicEq
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.IconButtonDefaults
import androidx.compose.material3.IconToggleButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.ListItem
import androidx.compose.material3.ListItemDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Slider
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import coil3.compose.AsyncImage
import dev.wiormiw.oldcountry.data.RepeatMode
import dev.wiormiw.oldcountry.data.TimeSpan
import dev.wiormiw.oldcountry.playback.QueueItem
import dev.wiormiw.oldcountry.playback.thumbnailUrl

/** The song's YouTube picture, or a note when it has none. */
@Composable
fun Artwork(videoId: String?, size: Dp?, modifier: Modifier = Modifier) {
    val shape = RoundedCornerShape(if (size != null && size < 64.dp) 6.dp else 12.dp)
    val sized = (if (size != null) modifier.size(size) else modifier.fillMaxWidth().aspectRatio(1f))
        .clip(shape)
        .background(MaterialTheme.colorScheme.surfaceVariant)
    Box(sized, contentAlignment = Alignment.Center) {
        Icon(Icons.Filled.MusicNote, null, tint = MaterialTheme.colorScheme.onSurfaceVariant)
        if (videoId != null) {
            // YouTube's 4:3 thumbnails letterbox the 16:9 picture; zoom past the black bars.
            AsyncImage(
                thumbnailUrl(videoId), null,
                Modifier.matchParentSize().graphicsLayer(scaleX = 4f / 3, scaleY = 4f / 3),
                contentScale = ContentScale.Crop,
            )
        }
    }
}

/** The bar above the tabs: what's playing, play/pause and next. Tap it for the full player. */
@Composable
fun MiniPlayer(onOpen: () -> Unit) {
    val player = LocalEnv.current.player
    val state by player.state.collectAsState()
    val current = state.current ?: return
    Surface(color = MaterialTheme.colorScheme.surfaceContainerHigh) {
        Column {
            val duration = state.duration
            LinearProgressIndicator(
                progress = { if (duration != null && duration > 0) (state.position / duration).toFloat().coerceIn(0f, 1f) else 0f },
                modifier = Modifier.fillMaxWidth().height(2.dp),
                drawStopIndicator = {},
                gapSize = 0.dp,
            )
            Row(
                Modifier.fillMaxWidth().clickable(onClick = onOpen).padding(horizontal = 12.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Artwork(current.videoId, 44.dp)
                Spacer(Modifier.width(12.dp))
                Column(Modifier.weight(1f)) {
                    Text(current.title, maxLines = 1, overflow = TextOverflow.Ellipsis, fontWeight = FontWeight.Medium)
                    Text(
                        state.error ?: current.artist,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        style = MaterialTheme.typography.bodySmall,
                        color = if (state.error != null) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                PlayPause(state.playing, state.buffering, filled = false) { player.toggle() }
                IconButton(onClick = player::next) {
                    Icon(Icons.Filled.SkipNext, "Next")
                }
            }
        }
    }
}

@Composable
private fun PlayPause(playing: Boolean, buffering: Boolean, filled: Boolean, onClick: () -> Unit) {
    val content: @Composable () -> Unit = {
        when {
            buffering -> CircularProgressIndicator(Modifier.size(22.dp), strokeWidth = 2.dp)
            playing -> Icon(Icons.Filled.Pause, "Pause")
            else -> Icon(Icons.Filled.PlayArrow, "Play")
        }
    }
    if (filled) {
        FilledIconButton(onClick = onClick, modifier = Modifier.size(64.dp)) { content() }
    } else {
        IconButton(onClick = onClick) { content() }
    }
}

/** The full player: artwork, seek bar, every control, and the queue. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun NowPlayingSheet(onDismiss: () -> Unit) {
    val player = LocalEnv.current.player
    val state by player.state.collectAsState()
    val sheet = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = sheet) {
        val current = state.current
        LazyColumn(Modifier.fillMaxWidth(), contentPadding = androidx.compose.foundation.layout.PaddingValues(bottom = 24.dp)) {
            item {
                Column(Modifier.padding(horizontal = 24.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                    Artwork(current?.videoId, null, Modifier.padding(horizontal = 24.dp))
                    Spacer(Modifier.height(20.dp))
                    Text(
                        current?.title ?: "Nothing playing",
                        style = MaterialTheme.typography.titleLarge,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(current?.artist.orEmpty(), color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1)
                    state.error?.let {
                        Spacer(Modifier.height(8.dp))
                        Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
                    }
                    Spacer(Modifier.height(12.dp))
                    SeekBar(state.position, state.duration, enabled = current != null, onSeek = player::seek)
                    Row(
                        Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceEvenly,
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        IconToggleButton(checked = state.shuffle, onCheckedChange = player::setShuffle) {
                            Icon(Icons.Filled.Shuffle, if (state.shuffle) "Shuffle on" else "Shuffle off")
                        }
                        IconButton(onClick = player::previous, enabled = current != null) {
                            Icon(Icons.Filled.SkipPrevious, "Previous", Modifier.size(32.dp))
                        }
                        PlayPause(state.playing, state.buffering, filled = true) { player.toggle() }
                        IconButton(onClick = player::next, enabled = current != null) {
                            Icon(Icons.Filled.SkipNext, "Next", Modifier.size(32.dp))
                        }
                        IconToggleButton(checked = state.repeat != RepeatMode.Off, onCheckedChange = { player.cycleRepeat() }) {
                            Icon(
                                if (state.repeat == RepeatMode.One) Icons.Filled.RepeatOne else Icons.Filled.Repeat,
                                when (state.repeat) {
                                    RepeatMode.Off -> "Repeat off"
                                    RepeatMode.All -> "Repeat all"
                                    RepeatMode.One -> "Repeat this song"
                                },
                            )
                        }
                    }
                }
                Spacer(Modifier.height(16.dp))
                HorizontalDivider()
                Text(
                    "Queue · ${state.queue.size} song${if (state.queue.size == 1) "" else "s"}",
                    style = MaterialTheme.typography.titleSmall,
                    modifier = Modifier.padding(horizontal = 24.dp, vertical = 12.dp),
                )
            }
            itemsIndexed(state.queue) { i, item ->
                QueueRow(item, playing = i == state.index, onClick = { player.jump(i) }, onRemove = { player.remove(i) })
            }
        }
    }
}

@Composable
private fun SeekBar(position: Double, duration: Double?, enabled: Boolean, onSeek: (Double) -> Unit) {
    // While dragging, the thumb follows the finger; the song seeks on release.
    var dragging by remember { mutableStateOf<Float?>(null) }
    val total = duration?.takeIf { it > 0 }
    Column(Modifier.fillMaxWidth()) {
        Slider(
            value = dragging ?: (total?.let { (position / it).toFloat() } ?: 0f).coerceIn(0f, 1f),
            onValueChange = { dragging = it },
            onValueChangeFinished = {
                val to = dragging
                if (to != null && total != null) onSeek(to * total)
                dragging = null
            },
            enabled = enabled && total != null,
        )
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            val shown = dragging?.let { d -> total?.let { d * it } } ?: position
            Text(TimeSpan.display(shown), style = MaterialTheme.typography.labelSmall)
            Text(TimeSpan.display(total), style = MaterialTheme.typography.labelSmall)
        }
    }
}

@Composable
private fun QueueRow(item: QueueItem, playing: Boolean, onClick: () -> Unit, onRemove: () -> Unit) {
    ListItem(
        modifier = Modifier.clickable(onClick = onClick),
        leadingContent = {
            if (playing) {
                Box(Modifier.size(40.dp), contentAlignment = Alignment.Center) {
                    Icon(Icons.Filled.GraphicEq, "Playing", tint = MaterialTheme.colorScheme.primary)
                }
            } else {
                Artwork(item.videoId, 40.dp)
            }
        },
        headlineContent = {
            Text(
                item.title,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                color = if (playing) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface,
                fontWeight = if (playing) FontWeight.SemiBold else null,
            )
        },
        supportingContent = { Text(item.artist, maxLines = 1, overflow = TextOverflow.Ellipsis) },
        trailingContent = {
            IconButton(onClick = onRemove, colors = IconButtonDefaults.iconButtonColors()) { Icon(Icons.Filled.Close, "Remove from queue") }
        },
        colors = ListItemDefaults.colors(containerColor = MaterialTheme.colorScheme.surfaceContainerLow),
    )
}
