package dev.wiormiw.oldcountry.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import dev.wiormiw.oldcountry.data.ThemeMode

// The desktop app's two themes, after the legendary pair, kept old and warm rather than neon:
// light is "bone and ember" (Reshiram), dark is "soot and storm" (Zekrom).

private val Light = lightColorScheme(
    primary = Color(0xFFB5461C), // ember
    onPrimary = Color(0xFFFAF7F1),
    primaryContainer = Color(0xFFF3D6C6),
    onPrimaryContainer = Color(0xFF3D1607),
    secondary = Color(0xFF4D6B7C), // Reshiram's blue-grey
    onSecondary = Color(0xFFFAF7F1),
    secondaryContainer = Color(0xFFE6DFD3),
    onSecondaryContainer = Color(0xFF24211D),
    tertiary = Color(0xFF4A7A42),
    onTertiary = Color(0xFFFAF7F1),
    background = Color(0xFFF2EDE4), // bone
    onBackground = Color(0xFF24211D),
    surface = Color(0xFFF2EDE4),
    onSurface = Color(0xFF24211D),
    surfaceVariant = Color(0xFFE6DFD3),
    onSurfaceVariant = Color(0xFF6F6A62), // ash
    surfaceContainerLowest = Color(0xFFFFFDF9),
    surfaceContainerLow = Color(0xFFFAF7F1),
    surfaceContainer = Color(0xFFF6F2EB),
    surfaceContainerHigh = Color(0xFFEFE9DF),
    surfaceContainerHighest = Color(0xFFE6DFD3),
    surfaceBright = Color(0xFFFAF7F1),
    surfaceDim = Color(0xFFDDD5C8),
    outline = Color(0xFFB3AA9C),
    outlineVariant = Color(0xFFD8D0C2),
    error = Color(0xFFA12A1C),
    onError = Color(0xFFFAF7F1),
    inverseSurface = Color(0xFF2A2622),
    inverseOnSurface = Color(0xFFEFE9DF),
    inversePrimary = Color(0xFFF08A5D),
)

private val Dark = darkColorScheme(
    primary = Color(0xFF5AAAD3), // storm blue
    onPrimary = Color(0xFF0E0F11),
    primaryContainer = Color(0xFF173A4D),
    onPrimaryContainer = Color(0xFFD3ECF8),
    secondary = Color(0xFFD45F3C), // Zekrom's red eyes
    onSecondary = Color(0xFF0E0F11),
    secondaryContainer = Color(0xFF30343A),
    onSecondaryContainer = Color(0xFFEBE6DD),
    tertiary = Color(0xFF80BD76),
    onTertiary = Color(0xFF0E0F11),
    background = Color(0xFF121315), // soot
    onBackground = Color(0xFFEBE6DD),
    surface = Color(0xFF121315),
    onSurface = Color(0xFFEBE6DD),
    surfaceVariant = Color(0xFF25282C),
    onSurfaceVariant = Color(0xFF9B958C),
    surfaceContainerLowest = Color(0xFF0B0C0D),
    surfaceContainerLow = Color(0xFF16181A),
    surfaceContainer = Color(0xFF1A1C1F),
    surfaceContainerHigh = Color(0xFF212427),
    surfaceContainerHighest = Color(0xFF2A2D32),
    surfaceBright = Color(0xFF2A2D32),
    surfaceDim = Color(0xFF121315),
    outline = Color(0xFF5A5E64),
    outlineVariant = Color(0xFF30343A),
    error = Color(0xFFE57058),
    onError = Color(0xFF0E0F11),
    inverseSurface = Color(0xFFEBE6DD),
    inverseOnSurface = Color(0xFF24211D),
    inversePrimary = Color(0xFF2B6E91),
)

/** Headings in a serif: the "old country" in the name. Body text stays in the system font. */
private val Type = Typography().run {
    copy(
        displayLarge = displayLarge.copy(fontFamily = FontFamily.Serif),
        displayMedium = displayMedium.copy(fontFamily = FontFamily.Serif),
        displaySmall = displaySmall.copy(fontFamily = FontFamily.Serif),
        headlineLarge = headlineLarge.copy(fontFamily = FontFamily.Serif),
        headlineMedium = headlineMedium.copy(fontFamily = FontFamily.Serif),
        headlineSmall = headlineSmall.copy(fontFamily = FontFamily.Serif),
        titleLarge = titleLarge.copy(fontFamily = FontFamily.Serif),
    )
}

/** Whether the app is dark right now: the user's choice, or the system's. */
@Composable
fun isDark(mode: ThemeMode): Boolean = when (mode) {
    ThemeMode.System -> isSystemInDarkTheme()
    ThemeMode.Light -> false
    ThemeMode.Dark -> true
}

@Composable
fun OldCountryTheme(mode: ThemeMode = ThemeMode.System, content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = if (isDark(mode)) Dark else Light, typography = Type, content = content)
}
