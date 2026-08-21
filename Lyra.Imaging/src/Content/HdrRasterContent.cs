using SkiaSharp;

namespace Lyra.Imaging.Content;

/// <summary>
/// Scene-referred HDR pixels, kept in half-float and NOT tone-mapped.
///
/// Ordinary <see cref="RasterContent"/> holds display-ready 8-bit pixels: whatever curve and
/// exposure produced them is baked in, and changing either means decoding the file again. This
/// keeps the linear values instead and leaves brightness to the renderer's shader, so the tone
/// controls are live.
///
/// Derives from <see cref="RasterContent"/> so everything that just wants "the image" - sizing,
/// disposal, content-kind checks - keeps working unchanged. Only the drawer needs to know the
/// difference, and it checks for this type first.
///
/// Half rather than full float: 16 bits carries far more range than any display can show (and
/// more than the 8 bits this replaces), at half the memory. A 4K environment map is 64 MB
/// instead of 128 MB.
/// </summary>
public sealed class HdrRasterContent(SKBitmap backingBitmap, SKImage image, float whitePoint)
    : RasterContent(backingBitmap, image)
{
    /// <summary>
    /// The luminance Reinhard should map to white, measured from these pixels at decode time.
    /// Carried on the content because the renderer cannot afford to re-measure 8 million pixels
    /// every frame, and the answer never changes once the image is decoded.
    /// </summary>
    public float WhitePoint { get; } = whitePoint;
}
