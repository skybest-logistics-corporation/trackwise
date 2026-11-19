package com.trackwise.driver.ui.theme

import androidx.compose.ui.graphics.Color

// shadcn/ui "zinc" palette. The app's look comes from these: near-black primary,
// white or near-black surfaces, hairline zinc borders, muted zinc secondary text.
val Zinc50 = Color(0xFFFAFAFA)
val Zinc100 = Color(0xFFF4F4F5)
val Zinc200 = Color(0xFFE4E4E7)
val Zinc300 = Color(0xFFD4D4D8)
val Zinc400 = Color(0xFFA1A1AA)
val Zinc500 = Color(0xFF71717A)
val Zinc600 = Color(0xFF52525B)
val Zinc700 = Color(0xFF3F3F46)
val Zinc800 = Color(0xFF27272A)
val Zinc900 = Color(0xFF18181B)
val Zinc950 = Color(0xFF09090B)

// Destructive. Red-600 on light so error text stays readable in sunlight; red-500 on dark.
val Red600 = Color(0xFFDC2626)
val Red500 = Color(0xFFEF4444)
val Red50 = Color(0xFFFEF2F2)
val Red950 = Color(0xFF450A0A)

// Vehicle Diagram Colors
val DiagramBackground = Color(0xFFF5F5F5)
val DiagramBodyFill = Color(0xFFDDDDDD)
val DiagramBodyOutline = Color(0xFF555555)
val DiagramCabin = Color(0xFFCCCCCC)
val DiagramGlass = Color(0xFFADD8E6)
val DiagramGlassOutline = Color(0xFF777777)
val DiagramWheel = Color(0xFF2D2D2D)
val DiagramHeadlight = Color(0xFFFFEB3B)
val DiagramTaillight = Color(0xFFE53935)
val DiagramMirror = Color(0xFF555555)

// Damage Severity Colors
val DamageSevere = Color(0xFFD32F2F)
val DamageModerate = Color(0xFFFF9800)
val DamageMinor = Color(0xFFFFC107)

// Messaging Colors
val MessageReadCheck = Color(0xFF53BDEB)

// Signature Pad Colors
val SignatureStroke = Color.Black
val SignatureBackground = Color.White
val SignaturePlaceholder = Color.Gray

// Dark variants for diagram/signature
val DiagramBackgroundDark = Color(0xFF2C2C2C)
val DiagramBodyFillDark = Color(0xFF444444)
val DiagramBodyOutlineDark = Color(0xFFAAAAAA)
val DiagramCabinDark = Color(0xFF3A3A3A)
val DiagramGlassDark = Color(0xFF4A6A7A)
val DiagramGlassOutlineDark = Color(0xFF888888)
val DiagramWheelDark = Color(0xFF1A1A1A)
val SignatureStrokeDark = Color.White
val SignatureBackgroundDark = Color(0xFF2C2C2C)
val SignaturePlaceholderDark = Color(0xFF888888)

/** Today's colors tell the crew where they are: going, at a stop, done. They read in sunlight on both themes. */
val StepGo = Color(0xFF1F5FA8)
val StepAtStop = Color(0xFFC86A0A)
val StepDone = Color(0xFF1D7A46)
val StepProblem = Color(0xFFB42318)
val StepNeutral = Color(0xFF5D6661)
