using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

internal static class PixelCopy
{
    /// <summary>
    /// Copies tightly packed RGBA8 pixels (stride == width * 4) into <paramref name="bitmap"/>,
    /// honoring the bitmap's <see cref="SKBitmap.RowBytes"/>. SKBitmaps allocated from an
    /// SKImageInfo are tightly packed today, but that is an allocator detail, not a contract.
    /// </summary>
    public static unsafe void CopyTightRgba(ReadOnlySpan<byte> src, SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var tightRowBytes = width * 4;
        var dstRowBytes = bitmap.RowBytes;
        var dst = (byte*)bitmap.GetPixels();

        if (dstRowBytes == tightRowBytes)
        {
            var total = tightRowBytes * height;
            src[..total].CopyTo(new Span<byte>(dst, total));
            return;
        }

        for (var y = 0; y < height; y++)
        {
            src.Slice(y * tightRowBytes, tightRowBytes)
                .CopyTo(new Span<byte>(dst + (long)y * dstRowBytes, tightRowBytes));
        }
    }

    /// <summary>
    /// Copies rows of pixels into <paramref name="bitmap"/>, one at a time, taking as many bytes
    /// per row as its color type implies and stepping the source by its own stride.
    /// </summary>
    public static unsafe void CopyRows(IntPtr source, long sourceStride, SKBitmap bitmap)
    {
        var width = bitmap.Width * Math.Max(1, bitmap.ColorType.GetBytesPerPixel());
        var height = bitmap.Height;

        var src = (byte*)source;
        var dst = (byte*)bitmap.GetPixels();
        var dstStride = bitmap.RowBytes;

        for (long y = 0; y < height; y++)
            Buffer.MemoryCopy(src + y * sourceStride, dst + y * dstStride, dstStride, width);
    }

    /// <summary>
    /// Copies straight-alpha (unassociated) RGBA8 into a premultiplied RGBA8 bitmap, which is what
    /// Skia draws. Native decoders that hand back unassociated alpha - OpenJPEG, libjxl - all land
    /// here rather than each carrying the same loop.
    /// </summary>
    /// <param name="isGrayscale">
    /// Whether every pixel has R == G == B, which decoders publish as metadata. Measured here
    /// because the copy is already touching every pixel.
    /// </param>
    public static unsafe void CopyPremultiplyingRgba(IntPtr source, int sourceStride, SKBitmap bitmap, CancellationToken ct, out bool isGrayscale)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;

        bitmap.Erase(SKColors.Transparent);

        var src = (byte*)source;
        var dst = (byte*)bitmap.GetPixels();
        var dstStride = bitmap.RowBytes;

        if (IsOpaque(src, sourceStride, width, height))
        {
            CopyRows(source, sourceStride, bitmap);
            isGrayscale = IsGray(src, sourceStride, width, height);
            return;
        }

        var gray = true;

        for (var y = 0; y < height; y++)
        {
            if ((y & 0x3F) == 0)
                ct.ThrowIfCancellationRequested();

            var srcRow = src + (nint)y * sourceStride;
            var dstRow = dst + (nint)y * dstStride;

            for (var x = 0; x < width; x++)
            {
                var i = x * 4;

                var r = srcRow[i + 0];
                var g = srcRow[i + 1];
                var b = srcRow[i + 2];
                var a = srcRow[i + 3];

                if (gray && (g != r || b != r))
                    gray = false;

                if (a == 0)
                {
                    dstRow[i + 0] = 0;
                    dstRow[i + 1] = 0;
                    dstRow[i + 2] = 0;
                    dstRow[i + 3] = 0;
                }
                else if (a == 255)
                {
                    dstRow[i + 0] = r;
                    dstRow[i + 1] = g;
                    dstRow[i + 2] = b;
                    dstRow[i + 3] = 255;
                }
                else
                {
                    dstRow[i + 0] = (byte)((r * a + 127) / 255);
                    dstRow[i + 1] = (byte)((g * a + 127) / 255);
                    dstRow[i + 2] = (byte)((b * a + 127) / 255);
                    dstRow[i + 3] = a;
                }
            }
        }

        isGrayscale = gray;
    }

    private static unsafe bool IsOpaque(byte* src, int stride, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            var row = src + (nint)y * stride;

            for (var x = 0; x < width; x++)
                if (row[x * 4 + 3] != 255)
                    return false;
        }

        return true;
    }

    private static unsafe bool IsGray(byte* src, int stride, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            var row = src + (nint)y * stride;

            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                if (row[i + 1] != row[i] || row[i + 2] != row[i])
                    return false;
            }
        }

        return true;
    }
}