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
}