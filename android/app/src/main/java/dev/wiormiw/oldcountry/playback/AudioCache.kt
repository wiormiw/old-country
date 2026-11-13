package dev.wiormiw.oldcountry.playback

import android.content.Context
import android.net.Uri
import androidx.annotation.OptIn
import androidx.core.net.toUri
import androidx.media3.common.util.UnstableApi
import androidx.media3.database.StandaloneDatabaseProvider
import androidx.media3.datasource.DataSpec
import androidx.media3.datasource.ResolvingDataSource
import androidx.media3.datasource.cache.CacheDataSource
import androidx.media3.datasource.cache.CacheWriter
import androidx.media3.datasource.cache.ContentMetadata
import androidx.media3.datasource.cache.LeastRecentlyUsedCacheEvictor
import androidx.media3.datasource.cache.SimpleCache
import androidx.media3.datasource.okhttp.OkHttpDataSource
import dev.wiormiw.oldcountry.data.ApiClient
import dev.wiormiw.oldcountry.data.ApiException
import dev.wiormiw.oldcountry.data.SettingsStore
import okhttp3.OkHttpClient
import java.io.File
import java.io.IOException

/**
 * Songs played are kept on the phone (least recently played go first once the cache is full), so
 * replays start at once and play without the server. Keys are the song's, not its URL's, so a
 * changed server address keeps the cache.
 */
@OptIn(UnstableApi::class)
class AudioCache(context: Context, http: OkHttpClient, private val settings: SettingsStore) {
    // The size limit is read once: a new one applies from the next start.
    private val cache = SimpleCache(
        File(context.cacheDir, "audio"),
        LeastRecentlyUsedCacheEvictor(settings.settings.value.cacheMaxMegabytes * 1024 * 1024),
        StandaloneDatabaseProvider(context),
    )

    /** Turns `oldcountry://audio/<key>` into the server's URL for it, at the moment it's fetched. */
    private val resolving = ResolvingDataSource.Factory(OkHttpDataSource.Factory(http)) { spec ->
        val audio = AudioRef.fromUri(spec.uri) ?: return@Factory spec
        spec.withUri(serverUri(audio))
    }

    val dataSourceFactory: CacheDataSource.Factory = CacheDataSource.Factory()
        .setCache(cache)
        .setUpstreamDataSourceFactory(resolving)
        .setFlags(CacheDataSource.FLAG_IGNORE_CACHE_ON_ERROR)

    private fun serverUri(audio: AudioRef): Uri = try {
        (ApiClient.parseBase(settings.settings.value.serverUrl).toString().trimEnd('/') + audio.path).toUri()
    } catch (e: ApiException) {
        throw IOException(e.message, e)
    }

    /** Whole song on the phone. */
    fun isCached(audio: AudioRef): Boolean {
        val length = ContentMetadata.getContentLength(cache.getContentMetadata(audio.key))
        return length > 0 && cache.isCached(audio.key, 0, length)
    }

    val usedBytes: Long get() = cache.cacheSpace

    fun clear() {
        cache.keys.toList().forEach(cache::removeResource)
    }

    /** Fetches a whole song into the cache (blocking; call off the main thread). Cancel with [CacheWriter.cancel]. */
    fun writer(audio: AudioRef): CacheWriter = CacheWriter(
        dataSourceFactory.createDataSource(),
        DataSpec.Builder().setUri(audio.uri).setKey(audio.key).build(),
        null,
        null,
    )
}
