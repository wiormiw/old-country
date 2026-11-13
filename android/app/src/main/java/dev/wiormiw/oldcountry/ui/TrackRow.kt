package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import dev.wiormiw.oldcountry.data.TimeSpan

/** One action in a song's ⋮ menu. */
data class RowAction(val label: String, val onClick: () -> Unit)

/** A song in a list: tap to play, ⋮ for the rest. */
@Composable
fun SongRow(
    title: String,
    artist: String,
    duration: Double?,
    videoId: String?,
    onPlay: () -> Unit,
    actions: List<RowAction>,
    badges: @Composable () -> Unit = {},
    playing: Boolean = false,
) {
    ListItem(
        modifier = Modifier.clickable(onClick = onPlay),
        leadingContent = { Artwork(videoId, 44.dp) },
        headlineContent = {
            Text(
                title,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                color = if (playing) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface,
            )
        },
        supportingContent = {
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(
                    listOf(artist, TimeSpan.display(duration)).filter(String::isNotBlank).joinToString(" · "),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f, fill = false),
                )
                badges()
            }
        },
        trailingContent = { if (actions.isNotEmpty()) RowMenu(actions) },
    )
}

@Composable
fun RowMenu(actions: List<RowAction>) {
    var open by remember { mutableStateOf(false) }
    Box {
        IconButton(onClick = { open = true }) { Icon(Icons.Filled.MoreVert, "More") }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            actions.forEach { action ->
                DropdownMenuItem(text = { Text(action.label) }, onClick = {
                    open = false
                    action.onClick()
                })
            }
        }
    }
}
