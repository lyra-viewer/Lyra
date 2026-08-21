using Lyra.Imaging.Content;
using Lyra.ManagedCodecs.Texture;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Shared DDS/KTX surface-to-bitmap conversion: HDR surfaces go through <see cref="HdrToneMap"/>,
/// LDR surfaces decode straight into the bitmap when its rows are tightly packed, else via a
/// temporary buffer that is then copied row-by-row.
/// </summary>
internal static class TextureBitmap
{
    /// <summary>
    /// Decodes a surface into displayable content. HDR surfaces (float formats, BC6H) stay
    /// scene-referred so the renderer's tone mapping applies to them exactly as it does to EXR;
    /// LDR surfaces become ordinary 8-bit raster content.
    ///
    /// <paramref name="flipVertical"/> handles bottom-up (OpenGL-convention) sources. It is done
    /// here rather than by the caller because the HDR path never materializes an 8-bit bitmap for
    /// the caller to flip.
    /// </summary>
    public static ICompositeContent DecodeToContent(TextureData texture, in Subresource surface, Composite composite, CancellationToken ct, bool flipVertical)
    {
        if (texture.IsHdr)
        {
            var floats = new float[surface.Width * surface.Height * 4];
            texture.DecodeHdr(surface, floats);

            if (flipVertical)
                FlipFloatRows(floats, surface.Width, surface.Height);

            return HdrImageBuilder.Build(floats, surface.Width, surface.Height, composite, ct, out _);
        }

        var bitmap = DecodeToBitmap(texture, surface, ct);

        if (flipVertical)
            FlipBitmapRows(bitmap);

        return RasterContentBuilder.Build(bitmap, composite);
    }

    private static void FlipFloatRows(float[] pixels, int width, int height)
    {
        var rowLength = width * 4;
        var row = new float[rowLength];

        for (var y = 0; y < height / 2; y++)
        {
            var top = y * rowLength;
            var bottom = (height - 1 - y) * rowLength;

            Array.Copy(pixels, top, row, 0, rowLength);
            Array.Copy(pixels, bottom, pixels, top, rowLength);
            Array.Copy(row, 0, pixels, bottom, rowLength);
        }
    }

    /// <summary>Flips a decoded surface in place. Shared so the thumbnail path cannot drift from this one.</summary>
    public static unsafe void FlipBitmapRows(SKBitmap bitmap)
    {
        var height = bitmap.Height;
        var rowBytes = bitmap.RowBytes;
        var pixels = (byte*)bitmap.GetPixels();
        var row = new byte[rowBytes];

        fixed (byte* tmp = row)
        {
            for (var y = 0; y < height / 2; y++)
            {
                var top = pixels + ((nint)y * rowBytes);
                var bottom = pixels + ((nint)(height - 1 - y) * rowBytes);

                Buffer.MemoryCopy(top, tmp, rowBytes, rowBytes);
                Buffer.MemoryCopy(bottom, top, rowBytes, rowBytes);
                Buffer.MemoryCopy(tmp, bottom, rowBytes, rowBytes);
            }
        }
    }

    public static SKBitmap DecodeToBitmap(TextureData texture, in Subresource surface, CancellationToken ct)
    {
        var info = new SKImageInfo(surface.Width, surface.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);

        if (texture.IsHdr)
        {
            var floats = new float[surface.Width * surface.Height * 4];
            texture.DecodeHdr(surface, floats);
            HdrToneMap.ToBitmap(floats, bitmap, ct, out _);
            return bitmap;
        }

        var tightRowBytes = surface.Width * 4;
        if (bitmap.RowBytes == tightRowBytes)
        {
            unsafe
            {
                texture.Decode(surface, new Span<byte>((void*)bitmap.GetPixels(), tightRowBytes * surface.Height));
            }
        }
        else
        {
            var tmp = new byte[tightRowBytes * surface.Height];
            texture.Decode(surface, tmp);
            PixelCopy.CopyTightRgba(tmp, bitmap);
        }

        return bitmap;
    }
}