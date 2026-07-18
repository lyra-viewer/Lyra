using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Tone-maps linear, scene-referred RGBA float pixels into an 8-bit RGBA <see cref="SKBitmap"/> using
/// the ACES filmic curve (Narkowicz 2015) plus a gamma 2.2 display encode. Shared by every HDR-ish
/// decode path (EXR, Radiance HDR, JXL, BC6H) so they all roll off highlights identically.
///
/// The per-pixel tone-map is the hotspot, so it is fanned out across rows; the gamma encode is a
/// 64 KB LUT instead of a per-channel MathF.Pow (≤1 byte-level error, even in deep shadows).
/// </summary>
internal static class HdrToneMap
{
    /// <summary>
    /// Writes the tone-mapped result into <paramref name="bitmap"/> (which must be RGBA8888 and match
    /// the implied dimensions of <paramref name="rgba"/>). <paramref name="isGrayscale"/> reports
    /// whether every pixel has R == G == B; single-channel sources (Y-only EXR, R16F/R32F textures,
    /// grayscale JXL) all replicate into RGB before reaching this point, so no broadcast happens here.
    /// </summary>
    public static void ToBitmap(Span<float> rgba, SKBitmap bitmap, CancellationToken ct, out bool isGrayscale)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;

        unsafe
        {
            fixed (float* srcPin = rgba)
            {
                var dstPin = (byte*)bitmap.GetPixels();
                var src = (nint)srcPin;
                var dst = (nint)dstPin;
                var dstRowBytes = bitmap.RowBytes;
                var options = new ParallelOptions { CancellationToken = ct };

                var gray = true;

                Parallel.For(0, height, options, y =>
                {
                    if (!ConvertRow((float*)src, (byte*)dst, y, width, dstRowBytes))
                        gray = false;
                });

                isGrayscale = gray;
            }
        }
    }

    /// <summary>Converts one row; returns true when every pixel in the row has R == G == B.</summary>
    private static unsafe bool ConvertRow(float* src, byte* dst, int y, int width, int dstRowBytes)
    {
        var srcRow = src + (nint)y * width * 4;
        var dstRow = dst + (nint)y * dstRowBytes;
        var gray = true;

        for (var x = 0; x < width; x++)
        {
            var idx = x * 4;
            var r = srcRow[idx];
            var g = srcRow[idx + 1];
            var b = srcRow[idx + 2];
            var a = srcRow[idx + 3];

            if (g != r || b != r)
                gray = false;

            dstRow[idx + 0] = ToneMap(r);
            dstRow[idx + 1] = ToneMap(g);
            dstRow[idx + 2] = ToneMap(b);
            dstRow[idx + 3] = AlphaToByte(a);
        }

        return gray;
    }

    private const int GammaLutSize = 65536;
    private static readonly byte[] GammaLut = BuildGammaLut();

    private static byte[] BuildGammaLut()
    {
        var lut = new byte[GammaLutSize];
        for (var i = 0; i < GammaLutSize; i++)
        {
            var t = i / (float)(GammaLutSize - 1);
            lut[i] = (byte)Math.Clamp(MathF.Pow(t, 1f / 2.2f) * 255f, 0f, 255f);
        }

        return lut;
    }

    /// <summary>ACES filmic tone curve + gamma 2.2 (via LUT). NaN maps to 0 (black).</summary>
    private static byte ToneMap(float value)
    {
        if (float.IsNaN(value))
            return 0;

        var x = MathF.Max(value, 0f);

        // ACES filmic curve: (x * (a*x + b)) / (x * (c*x + d) + e).
        const float a = 2.51f;
        const float b = 0.03f;
        const float c = 2.43f;
        const float d = 0.59f;
        const float e = 0.14f;
        var toneMapped = Math.Clamp((x * ((a * x) + b)) / ((x * ((c * x) + d)) + e), 0f, 1f);

        return GammaLut[(int)((toneMapped * (GammaLutSize - 1)) + 0.5f)];
    }

    /// <summary>Alpha to 8 bits - NOT gamma-encoded. NaN is treated as fully opaque.</summary>
    private static byte AlphaToByte(float value)
    {
        if (float.IsNaN(value))
            return 255;

        return (byte)Math.Clamp(value * 255f, 0f, 255f);
    }
}