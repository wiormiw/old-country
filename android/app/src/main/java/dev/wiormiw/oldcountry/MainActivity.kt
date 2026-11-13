package dev.wiormiw.oldcountry

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
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
            OldCountryTheme { AppScaffold(player) }
        }
    }

    override fun onStart() {
        super.onStart()
        player.connect()
    }

    override fun onStop() {
        player.release()
        super.onStop()
    }
}
