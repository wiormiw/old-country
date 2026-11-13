package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import dev.wiormiw.oldcountry.BuildConfig
import dev.wiormiw.oldcountry.app
import dev.wiormiw.oldcountry.data.ApiException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

private val cacheSizes = listOf(512L, 1_024L, 2_048L, 4_096L, 8_192L)

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun SettingsScreen() {
    val env = LocalEnv.current
    val app = LocalContext.current.app
    val scope = rememberCoroutineScope()
    val focus = androidx.compose.ui.platform.LocalFocusManager.current
    val settings by env.settings.settings.collectAsState()
    var url by remember { mutableStateOf(settings.serverUrl) }
    var checking by remember { mutableStateOf(false) }
    var status by remember { mutableStateOf<Pair<Boolean, String>?>(null) }
    var used by remember { mutableLongStateOf(-1) }
    LaunchedEffect(Unit) { used = withContext(Dispatchers.IO) { app.audioCache.usedBytes } }

    Screen("Settings") { modifier ->
        Column(
            modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text("Server", style = MaterialTheme.typography.titleMedium)
            Text(
                "The address of your old-country server, as this phone reaches it: on the same Wi-Fi, use the computer's local address.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            OutlinedTextField(
                url, { url = it; status = null },
                label = { Text("Server address") },
                placeholder = { Text("http://192.168.1.10:5000") },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                modifier = Modifier.fillMaxWidth(),
            )
            Button(enabled = !checking && url.isNotBlank(), onClick = {
                focus.clearFocus()
                checking = true
                scope.launch {
                    status = try {
                        env.api.checkServer(url)
                        env.settings.setServer(url)
                        true to "Connected and saved."
                    } catch (e: ApiException) {
                        false to e.message
                    } finally {
                        checking = false
                    }
                }
            }) { Text(if (checking) "Checking…" else "Check and save") }
            status?.let { (ok, message) ->
                Text(message, color = if (ok) MaterialTheme.colorScheme.tertiary else MaterialTheme.colorScheme.error)
            }

            HorizontalDivider(Modifier.padding(vertical = 8.dp))
            Text("Songs kept on this phone", style = MaterialTheme.typography.titleMedium)
            Text(
                "Songs you play are kept so they replay at once and play without the server. " +
                    if (used >= 0) "Using %.0f MB.".format(used / 1_048_576.0) else "",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                cacheSizes.forEach { size ->
                    FilterChip(
                        selected = settings.cacheMaxMegabytes == size,
                        onClick = { scope.launch { env.settings.setCacheMax(size) } },
                        label = { Text(if (size < 1_024) "$size MB" else "${size / 1_024} GB") },
                    )
                }
            }
            Text(
                "A new size applies the next time the app starts.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            OutlinedButton(onClick = {
                scope.launch {
                    withContext(Dispatchers.IO) { app.audioCache.clear() }
                    used = withContext(Dispatchers.IO) { app.audioCache.usedBytes }
                    env.notice("Cleared the songs kept on this phone.")
                }
            }) { Text("Clear") }

            HorizontalDivider(Modifier.padding(vertical = 8.dp))
            val uri = LocalUriHandler.current
            Text("old-country ${BuildConfig.VERSION_NAME}", style = MaterialTheme.typography.titleMedium)
            OutlinedButton(onClick = { uri.openUri("https://github.com/wiormiw/old-country/releases") }) { Text("Releases on GitHub") }
        }
    }
}
