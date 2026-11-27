package com.trackwise.driver.util

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.media.ExifInterface
import java.io.ByteArrayOutputStream
import kotlin.math.max
import kotlin.math.roundToInt

/** Longest edge of an uploaded photo: plenty to read a seal number or a delivery receipt. */
private const val MaxPhotoEdgePx = 1600
private const val JpegQuality = 80

/**
 * Shrinks a full-size photo (often 3-8 MB) to a JPEG of a few hundred KB, upright, so it uploads
 * quickly on mobile data. Returns null when the bytes can't be decoded, so the caller keeps the original.
 */
internal fun shrinkPhoto(bytes: ByteArray): ByteArray? = try {
    val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
    BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
    val longestEdge = max(bounds.outWidth, bounds.outHeight)
    if (longestEdge <= 0) {
        null
    } else {
        // Decode at the largest power-of-two step that stays above the target, then scale exactly.
        var sampleSize = 1
        while (longestEdge / (sampleSize * 2) >= MaxPhotoEdgePx) sampleSize *= 2
        val decoded = BitmapFactory.decodeByteArray(bytes, 0, bytes.size, BitmapFactory.Options().apply { inSampleSize = sampleSize })
        decoded?.let { bitmap ->
            val scale = MaxPhotoEdgePx.toFloat() / max(bitmap.width, bitmap.height)
            val matrix = Matrix().apply {
                if (scale < 1f) postScale(scale, scale)
                postRotate(exifRotationDegrees(bytes))
            }
            val upright = Bitmap.createBitmap(bitmap, 0, 0, bitmap.width, bitmap.height, matrix, true)
            ByteArrayOutputStream().use { out ->
                upright.compress(Bitmap.CompressFormat.JPEG, JpegQuality, out)
                if (upright !== bitmap) upright.recycle()
                bitmap.recycle()
                out.toByteArray()
            }
        }
    }
} catch (e: Exception) {
    Logger.e("PhotoShrinker", "Failed to shrink photo, sending the original", e)
    null
} catch (e: OutOfMemoryError) {
    Logger.e("PhotoShrinker", "Out of memory shrinking photo, sending the original", e)
    null
}

/** Re-encoding drops the EXIF orientation tag, so the rotation it describes is applied to the pixels. */
private fun exifRotationDegrees(bytes: ByteArray): Float =
    when (ExifInterface(bytes.inputStream()).getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL)) {
        ExifInterface.ORIENTATION_ROTATE_90 -> 90f
        ExifInterface.ORIENTATION_ROTATE_180 -> 180f
        ExifInterface.ORIENTATION_ROTATE_270 -> 270f
        else -> 0f
    }
