using System.Buffers.Binary;
using SkiaSharp;

namespace Lyra.Imaging.Tests.Support;

/// <summary>
/// Real frame bitstreams for <c>WebpBuilder</c>: Skia encodes a still image and the image chunks
/// are lifted out of its container.
/// </summary>
internal static class WebpFrames
{
    public static byte[] Solid(int width, int height, SKColor color, bool lossless = true) => Encode(width, height, (_, _) => color, lossless);

    public static byte[] Encode(int width, int height, Func<int, int, SKColor> pixel, bool lossless = true)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            bitmap.SetPixel(x, y, pixel(x, y));

        var options = new SKWebpEncoderOptions(lossless ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy, 100);

        using var pixmap = bitmap.PeekPixels();
        using var data = pixmap.Encode(options) ?? throw new InvalidOperationException("Skia could not encode the frame.");

        return ImageChunks(data.ToArray());
    }

    /// <summary>The ALPH, VP8 and VP8L chunks of a still WebP, in order.</summary>
    private static byte[] ImageChunks(byte[] webp)
    {
        var chunks = new List<byte>();

        for (var pos = 12; pos + 8 <= webp.Length;)
        {
            var fourCc = webp.AsSpan(pos, 4);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(pos + 4));
            var length = 8 + size + (size & 1);

            if (fourCc.SequenceEqual("ALPH"u8) || fourCc.SequenceEqual("VP8 "u8) || fourCc.SequenceEqual("VP8L"u8))
                chunks.AddRange(webp.AsSpan(pos, Math.Min(length, webp.Length - pos)));

            pos += length;
        }

        return [.. chunks];
    }
}