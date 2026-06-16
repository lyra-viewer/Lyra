using Lyra.Common;
using Lyra.Imaging.Decoding;
using Lyra.Imaging.Pipeline;
using SkiaSharp;

namespace Lyra.Imaging;

/// <summary>
/// Decodes an image and returns a small grayscale (luma) buffer suitable for
/// perceptual hashing.
/// </summary>
public static class GrayscaleThumbnail
{
    // Intermediate decode target. A little larger than the hash grid so the final
    // box-down to width*height has some anti-aliasing headroom.
    private const int IntermediateMaxDimension = 32;

    public static byte[]? Decode(string path, int width, int height, CancellationToken ct = default)
    {
        if (width <= 0 || height <= 0)
            return null;

        var format = ImageFormat.GetImageFormat(Path.GetExtension(path));
        if (DecoderManager.GetDecoder(format) is not IThumbnailDecoder thumbnailDecoder)
            return null;

        SKBitmap? bitmap = null;
        try
        {
            bitmap = thumbnailDecoder.DecodeThumbnail(path, IntermediateMaxDimension, ct);
            return bitmap is null ? null : ToLuma(bitmap, width, height);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[GrayscaleThumbnail] Decode failed for '{path}': {ex.Message}");
            return null;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private static byte[]? ToLuma(SKBitmap source, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        using var dst = new SKBitmap(info);
        using var dstPixmap = dst.PeekPixels();
        using var srcPixmap = source.PeekPixels();
        if (dstPixmap is null || srcPixmap is null)
            return null;

        if (!srcPixmap.ScalePixels(dstPixmap, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)))
            return null;

        var pixels = dstPixmap.GetPixelSpan();
        var luma = new byte[width * height];

        for (int i = 0, p = 0; i < luma.Length; i++, p += 4)
        {
            int r = pixels[p];
            int g = pixels[p + 1];
            int b = pixels[p + 2];
            luma[i] = (byte)((r * 299 + g * 587 + b * 114) / 1000); // Rec.601 luma
        }

        return luma;
    }
}
