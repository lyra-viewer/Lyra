using Lyra.Imaging.Psd.Core.Decode.Composite;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using SkiaSharp;

namespace Lyra.Imaging.Psd.Adapters.SkiaSharp;

internal static class SkiaCompositeConverter
{
    public static SkiaCompositeResult Convert(ICompositeImage composite)
    {
        ArgumentNullException.ThrowIfNull(composite);

        return composite switch
        {
            SurfaceCompositeImage s => ConvertSurface(s),
            TiledCompositeImage t => ConvertTiled(t),
            _ => throw new NotSupportedException($"Unsupported composite type: {composite.GetType().FullName}")
        };
    }

    private static SkiaCompositeResult ConvertSurface(SurfaceCompositeImage s)
    {
        // Note: Do NOT dispose the Surface or the produced SKImage here.
        // Ownership stays with the caller.
        var img = ToSKImage(s.Surface, s.Format);
        return new SkiaCompositeResult.Single(img);
    }

    private static SkiaCompositeResult ConvertTiled(TiledCompositeImage t)
    {
        var images = new SKImage?[t.TileCount];

        var tiles = t.Tiles;
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i];
            if (tile is null)
            {
                images[i] = null;
                continue;
            }

            images[i] = ToSKImage(tile, t.Format);
        }

        return new SkiaCompositeResult.Tiled(t.Width, t.Height, t.TileWidth, t.TileHeight, t.TilesX, t.TilesY, images);
    }

    private static SKImage ToSKImage(RgbaSurface surface, SurfaceFormat format)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var colorType = format.PixelFormat switch
        {
            PixelFormat.Bgra8888 => SKColorType.Bgra8888,
            PixelFormat.Rgba8888 => SKColorType.Rgba8888,
            _ => throw new NotSupportedException($"Unsupported PixelFormat for Skia: {format.PixelFormat}")
        };

        var alphaType = format.AlphaType switch
        {
            AlphaType.Straight => SKAlphaType.Unpremul,
            AlphaType.Premultiplied => SKAlphaType.Premul,
            _ => SKAlphaType.Unpremul
        };

        var info = new SKImageInfo(surface.Width, surface.Height, colorType, alphaType);
        using var bmp = new SKBitmap(info);

        var dstPtr = bmp.GetPixels();
        if (dstPtr == IntPtr.Zero)
            throw new InvalidOperationException("SKBitmap has no pixel buffer.");

        // Copy row-by-row to handle different strides.
        // Assumes 4 bytes per pixel for BGRA/RGBA 8888.
        var bytesPerRow = checked(surface.Width * 4);

        unsafe
        {
            var dstBase = (byte*)dstPtr;
            var src = surface.Memory.Span;

            for (var y = 0; y < surface.Height; y++)
            {
                var srcRow = src.Slice(y * surface.Stride, bytesPerRow);
                var dstRow = new Span<byte>(dstBase + y * bmp.RowBytes, bytesPerRow);
                srcRow.CopyTo(dstRow);
            }
        }

        var image = SKImage.FromBitmap(bmp);
        return image ?? throw new InvalidOperationException("SKImage.FromBitmap returned null.");
    }
}