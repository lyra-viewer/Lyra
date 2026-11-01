using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Variants;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// A reduced copy of decoded content, owning its own pixels, so the original can be disposed. HDR
/// stays scene-referred half-float, so a sheet of HDR textures still follows the exposure controls.
/// </summary>
internal static class ContentThumbnail
{
    private static readonly SKSamplingOptions Reduce = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>A copy no larger than <paramref name="maxSide"/> on either side; null for content with no pixels to copy.</summary>
    public static ICompositeContent? Make(ICompositeContent content, int maxSide)
    {
        while (content is VariantContainer variants)
        {
            if (variants.Active is not { } active)
                return null;

            content = active;
        }

        switch (content)
        {
            case HdrRasterContent hdr:
                return Scaled(hdr.Image, maxSide) is { } hdrCopy
                    ? new HdrRasterContent(hdrCopy, SKImage.FromBitmap(hdrCopy), hdr.WhitePoint, hdr.Encoding) { Range = hdr.Range with { Curve = null } }
                    : null;

            case RasterContent raster:
                return Scaled(raster.Image, maxSide) is { } copy 
                    ? new RasterContent(copy, SKImage.FromBitmap(copy)) 
                    : null;

            case RasterLargeContent { PreviewImage: { } preview } large:
                if (Scaled(preview, maxSide) is not { } previewCopy)
                    return null;

                return large.PreviewWhitePoint is { } whitePoint
                    ? new HdrRasterContent(previewCopy, SKImage.FromBitmap(previewCopy), whitePoint, large.PreviewEncoding) { Range = large.Range with { Curve = null } }
                    : new RasterContent(previewCopy, SKImage.FromBitmap(previewCopy));

            default:
                return null;
        }
    }

    private static SKBitmap? Scaled(SKImage source, int maxSide)
    {
        if (source.Width <= 0 || source.Height <= 0)
            return null;

        var scale = Math.Min(1f, (float)maxSide / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        var colorType = source.ColorType == SKColorType.Unknown ? SKColorType.Rgba8888 : source.ColorType;
        var bitmap = new SKBitmap(new SKImageInfo(width, height, colorType, source.AlphaType, source.ColorSpace));

        using var pixmap = bitmap.PeekPixels();
        if (pixmap is null || !source.ScalePixels(pixmap, Reduce))
        {
            bitmap.Dispose();
            return null;
        }

        bitmap.SetImmutable();
        return bitmap;
    }
}