package dev.wiormiw.oldcountry.ui

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.BrightnessAuto
import androidx.compose.material.icons.filled.DarkMode
import androidx.compose.material.icons.filled.LightMode
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Icon
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SegmentedButtonDefaults
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import dev.wiormiw.oldcountry.data.ThemeMode
import kotlinx.coroutines.launch

/** System / Light / Dark. On Home and in Settings; the choice is saved and applies at once. */
@Composable
fun ThemeToggle(modifier: Modifier = Modifier) {
    val env = LocalEnv.current
    val current by env.settings.settings.collectAsState()
    val options = listOf(
        Triple(ThemeMode.System, Icons.Filled.BrightnessAuto, "System"),
        Triple(ThemeMode.Light, Icons.Filled.LightMode, "Light"),
        Triple(ThemeMode.Dark, Icons.Filled.DarkMode, "Dark"),
    )
    SingleChoiceSegmentedButtonRow(modifier) {
        options.forEachIndexed { i, (mode, icon, label) ->
            SegmentedButton(
                selected = current.theme == mode,
                onClick = { env.scope.launch { env.settings.setTheme(mode) } },
                shape = SegmentedButtonDefaults.itemShape(i, options.size),
                icon = { SegmentIcon(icon) },
                label = { Text(label) },
            )
        }
    }
}

@Composable
private fun SegmentIcon(icon: ImageVector) = Icon(icon, null, Modifier.size(SegmentedButtonDefaults.IconSize))
