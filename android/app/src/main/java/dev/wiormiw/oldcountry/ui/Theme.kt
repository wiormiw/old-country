package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// The desktop app's palette: deep walnut, cream and brass.

private val Light = lightColorScheme(
    primary = Color(0xFFA87B17),
    onPrimary = Color(0xFFFFFAF0),
    primaryContainer = Color(0xFFF1DFB0),
    onPrimaryContainer = Color(0xFF3B2416),
    secondary = Color(0xFF3B2416),
    onSecondary = Color(0xFFFFFAF0),
    secondaryContainer = Color(0xFFEFE4D0),
    onSecondaryContainer = Color(0xFF2B1D13),
    tertiary = Color(0xFF3F7A3A),
    onTertiary = Color(0xFFFFFAF0),
    background = Color(0xFFF6EFE2),
    onBackground = Color(0xFF2B1D13),
    surface = Color(0xFFF6EFE2),
    onSurface = Color(0xFF2B1D13),
    surfaceVariant = Color(0xFFEFE4D0),
    onSurfaceVariant = Color(0xFF7A6450),
    surfaceContainerLowest = Color(0xFFFFFDF8),
    surfaceContainerLow = Color(0xFFFFFAF0),
    surfaceContainer = Color(0xFFF9F2E5),
    surfaceContainerHigh = Color(0xFFF3EADA),
    surfaceContainerHighest = Color(0xFFEFE4D0),
    surfaceBright = Color(0xFFFFFAF0),
    surfaceDim = Color(0xFFE6DAC4),
    outline = Color(0xFFB9A585),
    outlineVariant = Color(0xFFDDCDB4),
    error = Color(0xFFA3361F),
    onError = Color(0xFFFFFAF0),
    inverseSurface = Color(0xFF3B2416),
    inverseOnSurface = Color(0xFFF1E6D3),
    inversePrimary = Color(0xFFDDB63B),
)

private val Dark = darkColorScheme(
    primary = Color(0xFFC9A227),
    onPrimary = Color(0xFF1C140E),
    primaryContainer = Color(0xFF4A3712),
    onPrimaryContainer = Color(0xFFF1E6D3),
    secondary = Color(0xFFB3997E),
    onSecondary = Color(0xFF1C140E),
    secondaryContainer = Color(0xFF3F2F22),
    onSecondaryContainer = Color(0xFFF1E6D3),
    tertiary = Color(0xFF7CBF74),
    onTertiary = Color(0xFF1C140E),
    background = Color(0xFF1C140E),
    onBackground = Color(0xFFF1E6D3),
    surface = Color(0xFF1C140E),
    onSurface = Color(0xFFF1E6D3),
    surfaceVariant = Color(0xFF2F2218),
    onSurfaceVariant = Color(0xFFB3997E),
    surfaceContainerLowest = Color(0xFF140E09),
    surfaceContainerLow = Color(0xFF211710),
    surfaceContainer = Color(0xFF251A12),
    surfaceContainerHigh = Color(0xFF2F2218),
    surfaceContainerHighest = Color(0xFF3A2B1F),
    surfaceBright = Color(0xFF3A2B1F),
    surfaceDim = Color(0xFF1C140E),
    outline = Color(0xFF6E5A47),
    outlineVariant = Color(0xFF3F2F22),
    error = Color(0xFFE0715A),
    onError = Color(0xFF1C140E),
    inverseSurface = Color(0xFFF1E6D3),
    inverseOnSurface = Color(0xFF2B1D13),
    inversePrimary = Color(0xFFA87B17),
)

@Composable
fun OldCountryTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = if (isSystemInDarkTheme()) Dark else Light, content = content)
}
