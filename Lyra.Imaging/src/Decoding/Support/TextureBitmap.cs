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