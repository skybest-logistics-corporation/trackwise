package com.trackwise.driver.ui.theme

import androidx.compose.material3.Typography
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.em
import androidx.compose.ui.unit.sp
import org.jetbrains.compose.resources.Font
import trackwisedriver.composeapp.generated.resources.Res
import trackwisedriver.composeapp.generated.resources.geist_bold
import trackwisedriver.composeapp.generated.resources.geist_medium
import trackwisedriver.composeapp.generated.resources.geist_regular
import trackwisedriver.composeapp.generated.resources.geist_semibold

/** Geist, the typeface shadcn/ui's own site uses. Bundled (OFL) so it renders the same offline and on iOS. */
@Composable
fun geistFontFamily(): FontFamily = FontFamily(
    Font(Res.font.geist_regular, FontWeight.Normal),
    Font(Res.font.geist_medium, FontWeight.Medium),
    Font(Res.font.geist_semibold, FontWeight.SemiBold),
    Font(Res.font.geist_bold, FontWeight.Bold)
)

/**
 * shadcn-style type: semibold headings with slightly tight tracking, untracked body text, medium labels.
 * Sizes stay at Material's scale (body 16/14) so text reads in a truck cab; only the look changes.
 */
@Composable
fun trackwiseTypography(): Typography {
    val geist = geistFontFamily()
    return remember(geist) {
        fun style(size: Int, line: Int, weight: FontWeight, tracking: TextUnit = 0.sp) = TextStyle(
            fontFamily = geist,
            fontWeight = weight,
            fontSize = size.sp,
            lineHeight = line.sp,
            letterSpacing = tracking
        )
        Typography(
            displayLarge = style(57, 64, FontWeight.Bold, (-0.025).em),
            displayMedium = style(45, 52, FontWeight.Bold, (-0.025).em),
            displaySmall = style(36, 44, FontWeight.Bold, (-0.025).em),
            headlineLarge = style(32, 40, FontWeight.SemiBold, (-0.025).em),
            headlineMedium = style(28, 36, FontWeight.SemiBold, (-0.025).em),
            headlineSmall = style(24, 32, FontWeight.SemiBold, (-0.02).em),
            titleLarge = style(22, 28, FontWeight.SemiBold, (-0.015).em),
            titleMedium = style(16, 24, FontWeight.SemiBold),
            titleSmall = style(14, 20, FontWeight.Medium),
            bodyLarge = style(16, 24, FontWeight.Normal),
            bodyMedium = style(14, 20, FontWeight.Normal),
            bodySmall = style(12, 16, FontWeight.Normal),
            labelLarge = style(14, 20, FontWeight.Medium),
            labelMedium = style(12, 16, FontWeight.Medium),
            labelSmall = style(11, 16, FontWeight.Medium)
        )
    }
}
