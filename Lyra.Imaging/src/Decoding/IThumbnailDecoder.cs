using SkiaSharp;

namespace Lyra.Imaging.Decoding;

/// <summary>
/// Optional capability for decoders that can produce a small image cheaply (native scaled
/// decode for JPEG/JPEG2000, render-at-size for SVG) without the full Composite pipeline.
/// Implementing it is the single switch that opts a format into perceptual hashing; formats
/// whose decoder lacks it are skipped (e.g. PSD, which is excluded anyway).
/// </summary>
internal interface IThumbnailDecoder
{
    /// <summary>Decodes to a small raster (larger side ≤ maxDimension), or null. Caller disposes.</summary>
    SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct);
}
