package dev.wiormiw.oldcountry.data

import android.content.Context
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.flow.first

enum class RepeatMode { Off, All, One }

data class Settings(
    /** Base URL of the old-country API, e.g. `http://192.168.1.10:5000`. */
    val serverUrl: String = "",
    /** Largest size of the on-phone audio cache, in megabytes. */
    val cacheMaxMegabytes: Long = 2_048,
    val shuffle: Boolean = false,
    val repeat: RepeatMode = RepeatMode.Off,
)

private val Context.store by preferencesDataStore("settings")

class SettingsStore(context: Context, scope: CoroutineScope) {
    private val store = context.store

    private object Keys {
        val serverUrl = stringPreferencesKey("serverUrl")
        val cacheMax = longPreferencesKey("cacheMaxMegabytes")
        val shuffle = booleanPreferencesKey("shuffle")
        val repeat = stringPreferencesKey("repeat")
    }

    private fun read(prefs: Preferences) = Settings(
        serverUrl = prefs[Keys.serverUrl].orEmpty(),
        cacheMaxMegabytes = prefs[Keys.cacheMax] ?: Settings().cacheMaxMegabytes,
        shuffle = prefs[Keys.shuffle] ?: false,
        repeat = prefs[Keys.repeat]?.let { runCatching { RepeatMode.valueOf(it) }.getOrNull() } ?: RepeatMode.Off,
    )

    /** Read once at start-up (a small file), so the first screen already knows the server. */
    private val initial = runBlocking { read(store.data.first()) }

    val settings: StateFlow<Settings> = store.data.map(::read).stateIn(scope, SharingStarted.Eagerly, initial)

    suspend fun setServer(url: String) = store.edit { it[Keys.serverUrl] = url.trim().trimEnd('/') }
    suspend fun setCacheMax(megabytes: Long) = store.edit { it[Keys.cacheMax] = megabytes }
    suspend fun setShuffle(on: Boolean) = store.edit { it[Keys.shuffle] = on }
    suspend fun setRepeat(mode: RepeatMode) = store.edit { it[Keys.repeat] = mode.name }
}
