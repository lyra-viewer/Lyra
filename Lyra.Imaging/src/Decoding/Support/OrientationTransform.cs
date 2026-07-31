using Lyra.Common;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Rewrites decoded pixels so that the stored origin becomes top-left, i.e. so that the image
/// is upright without the renderer having to know anything about EXIF.
///
/// Done as a plain pixel copy rather than through an SKCanvas on purpose: the mapping is an
/// exact permutation of pixels, so there is no sampling, no premultiply/unpremultiply round
/// trip, and no dependency on canvas support for the target's alpha type or color space.
/// </summary>
internal static class OrientationTransform
{
    /// <summary>True for the four origins that transpose the image, swapping width and height.</summary>
    public static bool SwapsAxes(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <summary>
    /// Returns an upright copy of <paramref name="source"/> and disposes it, or returns
    /// <paramref name="source"/> untouched when there is nothing to do or the transform cannot
    /// be applied safely.
    /// </summary>
    public static SKBitmap Apply(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
            return source;
        
        if (source.ColorType != SKColorType.Rgba8888)
        {
            Logger.Warning($"[OrientationTransform] Origin {origin} not applied: unsupported color type {source.ColorType}.");
            return source;
        }

        var width = source.Width;
        var height = source.Height;
        if (width <= 0 || height <= 0)
            return source;

        var swaps = SwapsAxes(origin);
        var info = new SKImageInfo(
            swaps ? height : width,
            swaps ? width : height,
            source.ColorType,
            source.AlphaType,
            source.ColorSpace
        );

        var target = new SKBitmap(info);

        if (!Copy(source, target, origin))
        {
            target.Dispose();
            Logger.Warning($"[OrientationTransform] Origin {origin} not applied: pixel access failed.");
            return source;
        }

        source.Dispose();
        return target;
    }
    
    private static unsafe bool Copy(SKBitmap source, SKBitmap target, SKEncodedOrigin origin)
    {
        var src = (byte*)source.GetPixels();
        var dst = (byte*)target.GetPixels();
        if (src is null || dst is null)
            return false;

        var width = source.Width;
        var height = source.Height;
        var srcRowBytes = source.RowBytes;
        var dstRowBytes = target.RowBytes;

        var (ax, bx, cx, ay, by, cy) = Coefficients(origin, width, height);

        for (var y = 0; y < height; y++)
        {
            var srcPixels = (uint*)(src + (long)y * srcRowBytes);
            var dx = bx * y + cx;
            var dy = by * y + cy;

            for (var x = 0; x < width; x++, dx += ax, dy += ay)
            {
                *(uint*)(dst + (long)dy * dstRowBytes + (long)dx * 4) = srcPixels[x];
            }
        }

        return true;
    }
    
    private static (int Ax, int Bx, int Cx, int Ay, int By, int Cy) Coefficients(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight    => (-1,  0, width - 1,   0,  1, 0),           // mirror horizontal
        SKEncodedOrigin.BottomRight => (-1,  0, width - 1,   0, -1, height - 1),  // rotate 180
        SKEncodedOrigin.BottomLeft  => ( 1,  0, 0,           0, -1, height - 1),  // mirror vertical
        SKEncodedOrigin.LeftTop     => ( 0,  1, 0,           1,  0, 0),           // transpose
        SKEncodedOrigin.RightTop    => ( 0, -1, height - 1,  1,  0, 0),           // rotate 90 CW
        SKEncodedOrigin.RightBottom => ( 0, -1, height - 1, -1,  0, width - 1),   // transverse
        SKEncodedOrigin.LeftBottom  => ( 0,  1, 0,          -1,  0, width - 1),   // rotate 270 CW
        _                           => ( 1,  0, 0,           0,  1, 0)            // identity
    };
}