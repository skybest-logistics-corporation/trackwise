package com.trackwise.driver.ui.theme

import androidx.compose.ui.unit.dp

/**
 * Spacing tokens used uniformly across the app.
 * All raw dp values in screens should reference these instead of literals.
 */
object Spacing {
    val xxs = 2.dp
    val xs = 4.dp
    val sm = 8.dp
    val md = 12.dp
    val lg = 16.dp
    val xl = 24.dp
    val xxl = 32.dp
    val xxxl = 48.dp
}

/**
 * Radius tokens, following shadcn/ui (--radius 0.5rem): sm 6, md 8 (inputs, buttons), lg 12 (cards), xl 16 (sheets).
 */
object Radius {
    val sm = 6.dp
    val md = 8.dp
    val lg = 12.dp
    val xl = 16.dp
}

/** shadcn draws edges with a 1dp hairline border instead of shadows. */
object Border {
    val hairline = 1.dp
}
