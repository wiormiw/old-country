package dev.wiormiw.oldcountry

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.SystemBarStyle
import androidx.activity.enableEdgeToEdge
import android.graphics.Color
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import dev.wiormiw.oldcountry.ui.isDark
import kotlinx.coroutines.flow.map
import dev.wiormiw.oldcountry.playback.PlayerConnection
import dev.wiormiw.oldcountry.ui.AppScaffold
import dev.wiormiw.oldcountry.ui.OldCountryTheme
import androidx.lifecycle.lifecycleScope

class MainActivity : ComponentActivity() {
    private lateinit var player: PlayerConnection

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        player = PlayerConnection(this, lifecycleScope)
        setContent {
            val mode by app.settings.settings.map { it.theme }.collectAsState(app.settings.settings.value.theme)
            val dark = isDark(mode)
            // Status and navigation bar icons follow the app's theme, not only the system's.
            DisposableEffect(dark) {
                val bars = SystemBarStyle.auto(Color.TRANSPARENT, Color.TRANSPARENT) { dark }
                enableEdgeToEdge(statusBarStyle = bars, navigationBarStyle = bars)
                onDispose {}
            }
            OldCountryTheme(mode) { AppScaffold(player) }
        }
    }

    override fun onStart() {
        super.onStart()
        player.connect()
        // Live updates only while on screen: no battery spent on a connection nobody looks at.
        // Coming back reconnects, and the reconnect makes every screen catch up.
        app.live.start()
    }

    override fun onStop() {
        player.release()
        app.live.stop()
        super.onStop()
    }
}
