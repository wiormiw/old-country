package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.QueueMusic
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.LibraryMusic
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextOverflow
import androidx.navigation.NavDestination.Companion.hasRoute
import androidx.navigation.NavDestination.Companion.hierarchy
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import androidx.navigation.toRoute
import dev.wiormiw.oldcountry.app
import dev.wiormiw.oldcountry.playback.PlayerConnection
import kotlinx.serialization.Serializable
import kotlin.reflect.KClass

object Route {
    @Serializable data object Library
    @Serializable data class Track(val id: String)
    @Serializable data object Playlists
    @Serializable data class Playlist(val id: String)
    @Serializable data object YouTube
    @Serializable data object Downloads
    @Serializable data object Settings
}

private data class Tab(val label: String, val icon: ImageVector, val route: Any, val type: KClass<*>)

private val tabs = listOf(
    Tab("Library", Icons.Filled.LibraryMusic, Route.Library, Route.Library::class),
    Tab("Playlists", Icons.AutoMirrored.Filled.QueueMusic, Route.Playlists, Route.Playlists::class),
    Tab("YouTube", Icons.Filled.Search, Route.YouTube, Route.YouTube::class),
    Tab("Downloads", Icons.Filled.Download, Route.Downloads, Route.Downloads::class),
    Tab("Settings", Icons.Filled.Settings, Route.Settings, Route.Settings::class),
)

@Composable
fun AppScaffold(player: PlayerConnection) {
    val app = LocalContext.current.app
    val nav = rememberNavController()
    val snackbar = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    val env = remember(nav) { AppEnv(app.api, app.settings, player, nav, snackbar, scope) }
    // The first run starts in Settings, to say where the server is.
    val start: Any = remember { if (app.settings.settings.value.serverUrl.isBlank()) Route.Settings else Route.Library }
    var showPlayer by rememberSaveable { mutableStateOf(false) }

    CompositionLocalProvider(LocalEnv provides env) {
        Scaffold(
            contentWindowInsets = WindowInsets(0),
            snackbarHost = { SnackbarHost(snackbar) },
            bottomBar = {
                Column {
                    MiniPlayer(onOpen = { showPlayer = true })
                    val entry by nav.currentBackStackEntryAsState()
                    NavigationBar {
                        tabs.forEach { tab ->
                            val selected = entry?.destination?.hierarchy?.any { it.hasRoute(tab.type) } == true ||
                                (tab.type == Route.Library::class && entry?.destination?.hasRoute(Route.Track::class) == true) ||
                                (tab.type == Route.Playlists::class && entry?.destination?.hasRoute(Route.Playlist::class) == true)
                            NavigationBarItem(
                                selected = selected,
                                onClick = {
                                    nav.navigate(tab.route) {
                                        popUpTo(nav.graph.findStartDestination().id) { saveState = true }
                                        launchSingleTop = true
                                        restoreState = true
                                    }
                                },
                                icon = { Icon(tab.icon, null) },
                                label = { Text(tab.label, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                            )
                        }
                    }
                }
            },
        ) { padding ->
            NavHost(nav, startDestination = start, modifier = Modifier.padding(padding)) {
                composable<Route.Library> { LibraryScreen() }
                composable<Route.Track> { TrackScreen(it.toRoute<Route.Track>().id) }
                composable<Route.Playlists> { PlaylistsScreen() }
                composable<Route.Playlist> { PlaylistScreen(it.toRoute<Route.Playlist>().id) }
                composable<Route.YouTube> { YouTubeScreen() }
                composable<Route.Downloads> { DownloadsScreen() }
                composable<Route.Settings> { SettingsScreen() }
            }
        }
        if (showPlayer) NowPlayingSheet(onDismiss = { showPlayer = false })
    }
}

/** A screen with its title bar. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun Screen(
    title: String,
    back: Boolean = false,
    actions: @Composable androidx.compose.foundation.layout.RowScope.() -> Unit = {},
    floating: @Composable () -> Unit = {},
    content: @Composable (Modifier) -> Unit,
) {
    val env = LocalEnv.current
    Scaffold(
        contentWindowInsets = WindowInsets(0),
        topBar = {
            TopAppBar(
                title = { Text(title, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                navigationIcon = {
                    if (back) {
                        androidx.compose.material3.IconButton(onClick = { env.nav.popBackStack() }) {
                            Icon(Icons.AutoMirrored.Filled.ArrowBack, "Back")
                        }
                    }
                },
                actions = actions,
            )
        },
        floatingActionButton = floating,
    ) { padding -> content(Modifier.padding(padding)) }
}
