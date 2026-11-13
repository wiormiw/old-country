package dev.wiormiw.oldcountry

import android.app.Application
import coil3.ImageLoader
import coil3.PlatformContext
import coil3.SingletonImageLoader
import coil3.network.okhttp.OkHttpNetworkFetcherFactory
import dev.wiormiw.oldcountry.data.ApiClient
import dev.wiormiw.oldcountry.data.SettingsStore
import dev.wiormiw.oldcountry.playback.AudioCache
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.Dispatchers
import okhttp3.OkHttpClient
import java.util.concurrent.TimeUnit

/** The app's shared objects, made once per process. */
class OldCountryApp : Application(), SingletonImageLoader.Factory {
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    val http: OkHttpClient by lazy {
        OkHttpClient.Builder()
            .connectTimeout(5, TimeUnit.SECONDS)
            // YouTube songs the server hasn't fetched yet take a while (yt-dlp) to start answering.
            .readTimeout(60, TimeUnit.SECONDS)
            .addInterceptor { chain ->
                chain.proceed(chain.request().newBuilder().header("User-Agent", "old-country-android/${BuildConfig.VERSION_NAME}").build())
            }
            .build()
    }

    val settings by lazy { SettingsStore(this, scope) }
    val api by lazy { ApiClient(http) { settings.settings.value.serverUrl } }
    val audioCache by lazy { AudioCache(this, http, settings) }

    override fun newImageLoader(context: PlatformContext): ImageLoader =
        ImageLoader.Builder(context).components { add(OkHttpNetworkFetcherFactory(callFactory = { http })) }.build()
}

val android.content.Context.app: OldCountryApp get() = applicationContext as OldCountryApp
