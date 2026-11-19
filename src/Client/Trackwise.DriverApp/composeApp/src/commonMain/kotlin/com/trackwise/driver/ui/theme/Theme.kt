package com.trackwise.driver.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color

@Immutable
data class ExtendedColorScheme(
    val diagramBackground: Color,
    val diagramBodyFill: Color,
    val diagramBodyOutline: Color,
    val diagramCabin: Color,
    val diagramGlass: Color,
    val diagramGlassOutline: Color,
    val diagramWheel: Color,
    val diagramHeadlight: Color,
    val diagramTaillight: Color,
    val diagramMirror: Color,
    val damageSevere: Color,
    val damageModerate: Color,
    val damageMinor: Color,
    val messageReadCheck: Color,
    val signatureStroke: Color,
    val signatureBackground: Color,
    val signaturePlaceholder: Color
)

private val LightExtendedColors = ExtendedColorScheme(
    diagramBackground = DiagramBackground,
    diagramBodyFill = DiagramBodyFill,
    diagramBodyOutline = DiagramBodyOutline,
    diagramCabin = DiagramCabin,
    diagramGlass = DiagramGlass,
    diagramGlassOutline = DiagramGlassOutline,
    diagramWheel = DiagramWheel,
    diagramHeadlight = DiagramHeadlight,
    diagramTaillight = DiagramTaillight,
    diagramMirror = DiagramMirror,
    damageSevere = DamageSevere,
    damageModerate = DamageModerate,
    damageMinor = DamageMinor,
    messageReadCheck = MessageReadCheck,
    signatureStroke = SignatureStroke,
    signatureBackground = SignatureBackground,
    signaturePlaceholder = SignaturePlaceholder
)

private val DarkExtendedColors = ExtendedColorScheme(
    diagramBackground = DiagramBackgroundDark,
    diagramBodyFill = DiagramBodyFillDark,
    diagramBodyOutline = DiagramBodyOutlineDark,
    diagramCabin = DiagramCabinDark,
    diagramGlass = DiagramGlassDark,
    diagramGlassOutline = DiagramGlassOutlineDark,
    diagramWheel = DiagramWheelDark,
    diagramHeadlight = DiagramHeadlight,
    diagramTaillight = DiagramTaillight,
    diagramMirror = DiagramBodyOutlineDark,
    damageSevere = DamageSevere,
    damageModerate = DamageModerate,
    damageMinor = DamageMinor,
    messageReadCheck = MessageReadCheck,
    signatureStroke = SignatureStrokeDark,
    signatureBackground = SignatureBackgroundDark,
    signaturePlaceholder = SignaturePlaceholderDark
)

val LocalExtendedColors = staticCompositionLocalOf { LightExtendedColors }

// Material 3 roles mapped onto shadcn/ui zinc tokens. surfaceTint is transparent so elevated
// surfaces don't pick up Material's tinted look; containers stay flat and borders do the work.
private val LightColorScheme = lightColorScheme(
    primary = Zinc900,
    onPrimary = Zinc50,
    primaryContainer = Zinc100,
    onPrimaryContainer = Zinc900,
    inversePrimary = Zinc200,
    secondary = Zinc100,
    onSecondary = Zinc900,
    secondaryContainer = Zinc100,
    onSecondaryContainer = Zinc900,
    tertiary = Zinc700,
    onTertiary = Zinc50,
    tertiaryContainer = Zinc100,
    onTertiaryContainer = Zinc900,
    background = Color.White,
    onBackground = Zinc950,
    surface = Color.White,
    onSurface = Zinc950,
    surfaceVariant = Zinc100,
    onSurfaceVariant = Zinc600,
    surfaceTint = Color.Transparent,
    inverseSurface = Zinc900,
    inverseOnSurface = Zinc50,
    error = Red600,
    onError = Zinc50,
    errorContainer = Red50,
    onErrorContainer = Red600,
    outline = Zinc300,
    outlineVariant = Zinc200,
    scrim = Color.Black,
    surfaceBright = Color.White,
    surfaceDim = Zinc100,
    surfaceContainerLowest = Color.White,
    surfaceContainerLow = Color.White,
    surfaceContainer = Zinc50,
    surfaceContainerHigh = Color.White,
    surfaceContainerHighest = Zinc100
)

private val DarkColorScheme = darkColorScheme(
    primary = Zinc50,
    onPrimary = Zinc900,
    primaryContainer = Zinc800,
    onPrimaryContainer = Zinc50,
    inversePrimary = Zinc800,
    secondary = Zinc800,
    onSecondary = Zinc50,
    secondaryContainer = Zinc800,
    onSecondaryContainer = Zinc50,
    tertiary = Zinc300,
    onTertiary = Zinc900,
    tertiaryContainer = Zinc800,
    onTertiaryContainer = Zinc50,
    background = Zinc950,
    onBackground = Zinc50,
    surface = Zinc950,
    onSurface = Zinc50,
    surfaceVariant = Zinc800,
    onSurfaceVariant = Zinc400,
    surfaceTint = Color.Transparent,
    inverseSurface = Zinc50,
    inverseOnSurface = Zinc900,
    error = Red500,
    onError = Zinc50,
    errorContainer = Red950,
    onErrorContainer = Red500,
    outline = Zinc700,
    outlineVariant = Zinc800,
    scrim = Color.Black,
    surfaceBright = Zinc800,
    surfaceDim = Zinc950,
    surfaceContainerLowest = Zinc950,
    surfaceContainerLow = Zinc950,
    surfaceContainer = Zinc900,
    surfaceContainerHigh = Zinc900,
    surfaceContainerHighest = Zinc800
)

// shadcn radii (--radius 0.5rem): inputs/buttons 8, cards 12, sheets 16.
private val TrackwiseShapes = Shapes(
    extraSmall = RoundedCornerShape(Radius.sm),
    small = RoundedCornerShape(Radius.md),
    medium = RoundedCornerShape(Radius.md),
    large = RoundedCornerShape(Radius.lg),
    extraLarge = RoundedCornerShape(Radius.xl)
)

@Composable
fun TrackwiseDriverTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit
) {
    val colorScheme = when {
        darkTheme -> DarkColorScheme
        else -> LightColorScheme
    }
    val extendedColors = when {
        darkTheme -> DarkExtendedColors
        else -> LightExtendedColors
    }

    CompositionLocalProvider(LocalExtendedColors provides extendedColors) {
        MaterialTheme(
            colorScheme = colorScheme,
            typography = trackwiseTypography(),
            shapes = TrackwiseShapes,
            content = content
        )
    }
}
