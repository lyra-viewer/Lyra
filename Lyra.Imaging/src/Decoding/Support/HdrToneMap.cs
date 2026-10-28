using Lyra.Common.Settings.Enums;
using Lyra.Imaging.Content.Hdr;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Maps linear, scene-referred RGBA float pixels into an 8-bit RGBA <see cref="SKBitmap"/>.
/// Shared by every HDR-ish decode path (EXR, Radiance HDR, JXL, BC6H) so they all agree.
/// Which curve is used comes from <see cref="ToneMapMode"/>.
///
/// The per-pixel work is the hotspot, so it is fanned out across rows; the gamma encode is a
/// 64 KB LUT instead of a per-channel MathF.Pow (at most 1 byte-level error, even in shadows).
/// </summary>
internal static class HdrToneMap
{
    /// <summary>
    /// Writes the tone-mapped result into <paramref name="bitmap"/> (which must be RGBA8888 and
    /// match the implied dimensions of <paramref name="rgba"/>). <paramref name="isGrayscale"/>
    /// reports whether every pixel has R == G == B; single-channel sources (Y-only EXR, R16F/R32F
    /// textures, grayscale JXL) all replicate into RGB before reaching this point, so no
    /// broadcast happens here.
    /// </summary>
    public static void ToBitmap(Span<float> rgba, SKBitmap bitmap, CancellationToken ct, out bool isGrayscale) =>
        Convert(rgba, bitmap, HdrDecodeSettings.ToneMapMode, HdrDecodeSettings.ExposureScale, null, ct, out isGrayscale);
    
    public static void ToBitmap(Span<float> rgba, SKBitmap bitmap, float sceneWhitePoint, CancellationToken ct, out bool isGrayscale) =>
        Convert(rgba, bitmap, HdrDecodeSettings.ToneMapMode, HdrDecodeSettings.ExposureScale, sceneWhitePoint, ct, out isGrayscale);

    /// <summary>Overload taking explicit settings, so tests do not depend on the user's.</summary>
    public static void ToBitmap(Span<float> rgba, SKBitmap bitmap, ToneMapMode mode, float exposureScale, CancellationToken ct, out bool isGrayscale) =>
        Convert(rgba, bitmap, mode, exposureScale, null, ct, out isGrayscale);

    /// <summary>The same, from linear rows of any layout, landing <paramref name="range"/>'s scene white on white.</summary>
    public static void ToBitmap(LinearRows rows, SKBitmap bitmap, float sceneWhitePoint, SceneRange range, CancellationToken ct, out bool isGrayscale) =>
        Convert(rows, bitmap, HdrDecodeSettings.ToneMapMode, HdrDecodeSettings.ExposureScale, sceneWhitePoint, ct, out isGrayscale, range);

    /// <summary>Overload taking explicit settings, so tests do not depend on the user's.</summary>
    internal static void ToBitmap(LinearRows rows, SKBitmap bitmap, ToneMapMode mode, float exposureScale, SceneRange range, CancellationToken ct, out bool isGrayscale) =>
        Convert(rows, bitmap, mode, exposureScale, null, ct, out isGrayscale, range);

    private static unsafe void Convert(Span<float> rgba, SKBitmap bitmap, ToneMapMode mode, float exposureScale, float? sceneWhitePoint, CancellationToken ct, out bool isGrayscale)
    {
        if (rgba.Length < (long)bitmap.Width * bitmap.Height * 4)
            throw new ArgumentException("Fewer pixels than the bitmap holds.", nameof(rgba));

        fixed (float* pin = rgba)
            Convert(new FloatRgbaRows(pin, bitmap.Width, bitmap.Height), bitmap, mode, exposureScale, sceneWhitePoint, ct, out isGrayscale);
    }

    private static unsafe void Convert(LinearRows rows, SKBitmap bitmap, ToneMapMode mode, float exposureScale, float? sceneWhitePoint, CancellationToken ct, out bool isGrayscale, SceneRange range = default)
    {
        if (mode == ToneMapMode.Aces && range.Curve is { } curve)
        {
            ConvertWithCameraCurve(rows, bitmap, curve, exposureScale, ct, out isGrayscale);
            return;
        }

        var width = bitmap.Width;
        // Where the content says its light ends, every curve lands it on white.
        float? sceneWhite = range.SceneWhite is > 0f and var known ? known : null;

        var whitePoint = mode == ToneMapMode.ReinhardExtended
            ? (sceneWhite ?? sceneWhitePoint ?? MeasureWhitePoint(rows)) * exposureScale
            : 1f;

        var acesScale = sceneWhite is { } acesWhite ? 1f / Aces(acesWhite) : 1f;

        if (mode == ToneMapMode.Clip && sceneWhite is { } clipWhite)
            exposureScale /= clipWhite;

        var dst = (nint)bitmap.GetPixels();
        var dstRowBytes = bitmap.RowBytes;

        isGrayscale = rows.AllRows(ct, (y, row) =>
            ConvertRow(row, (byte*)dst + (nint)y * dstRowBytes, width, mode, whitePoint, exposureScale, acesScale));
    }

    /// <summary>
    /// The camera's own curve in place of the default one, as the shader applies it: in linear
    /// ProPhoto, on the largest and smallest channel, the middle one kept in proportion.
    /// </summary>
    private static unsafe void ConvertWithCameraCurve(LinearRows rows, SKBitmap bitmap, CameraToneCurve curve, float exposureScale, CancellationToken ct, out bool isGrayscale)
    {
        var width = bitmap.Width;
        var dst = (nint)bitmap.GetPixels();
        var dstRowBytes = bitmap.RowBytes;
        var to = ColorMatrix.ToCurveSpace;
        var from = ColorMatrix.FromCurveSpace;

        isGrayscale = rows.AllRows(ct, (y, row) =>
        {
            var gray = true;
            var dstRow = (byte*)dst + (nint)y * dstRowBytes;

            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                float r = Sanitize(row[i]) * exposureScale, g = Sanitize(row[i + 1]) * exposureScale, b = Sanitize(row[i + 2]) * exposureScale;

                if (row[i] != row[i + 1] || row[i + 2] != row[i])
                    gray = false;

                var wr = Math.Clamp(to[0] * r + to[1] * g + to[2] * b, 0f, 1f);
                var wg = Math.Clamp(to[3] * r + to[4] * g + to[5] * b, 0f, 1f);
                var wb = Math.Clamp(to[6] * r + to[7] * g + to[8] * b, 0f, 1f);

                var lo = MathF.Min(wr, MathF.Min(wg, wb));
                var hi = MathF.Max(wr, MathF.Max(wg, wb));
                var toneLo = curve.Evaluate(lo);
                var toneHi = curve.Evaluate(hi);
                var span = hi > lo ? (toneHi - toneLo) / (hi - lo) : 0f;

                wr = toneLo + (wr - lo) * span;
                wg = toneLo + (wg - lo) * span;
                wb = toneLo + (wb - lo) * span;

                dstRow[i] = Encode(from[0] * wr + from[1] * wg + from[2] * wb);
                dstRow[i + 1] = Encode(from[3] * wr + from[4] * wg + from[5] * wb);
                dstRow[i + 2] = Encode(from[6] * wr + from[7] * wg + from[8] * wb);
                dstRow[i + 3] = AlphaToByte(row[i + 3]);
            }

            return gray;
        });
    }

    /// <summary>Converts one row; returns true when every pixel in the row has R == G == B.</summary>
    private static unsafe bool ConvertRow(float* srcRow, byte* dstRow, int width, ToneMapMode mode, float whitePoint, float exposureScale, float acesScale)
    {
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

            ToneMapPixel(r, g, b, mode, whitePoint, exposureScale, acesScale, out var outR, out var outG, out var outB);

            dstRow[idx + 0] = outR;
            dstRow[idx + 1] = outG;
            dstRow[idx + 2] = outB;
            dstRow[idx + 3] = AlphaToByte(a);
        }

        return gray;
    }

    // ------------------------------------------------------------------
    //  White point
    // ------------------------------------------------------------------

    private const int HistogramBins = 512;
    private const float LogMin = -12f; // 2^-12, deep shadow
    private const float LogMax = 24f;  // 2^24, ~16.7 million - brighter than any sane sample

    /// <summary>Never sample more than this many pixels; beyond it the estimate stops improving.</summary>
    private const int WhitePointSampleBudget = 1 << 20; // ~1M
    
    internal static unsafe float MeasureWhitePoint(Span<float> rgba)
    {
        var pixels = rgba.Length / 4;
        if (pixels <= 0)
            return 1f;

        fixed (float* pin = rgba)
            return MeasureWhitePoint(new FloatRgbaRows(pin, pixels, 1));
    }

    internal static float MeasureWhitePoint(LinearRows rows)
    {
        var pixels = rows.PixelCount;
        if (pixels <= 0)
            return 1f;

        var stride = Math.Max(1, pixels / WhitePointSampleBudget);
        var samples = pixels / stride;
        const float scale = HistogramBins / (LogMax - LogMin);

        // One histogram per partition, merged at the end: no contention, no interlocked adds.
        var partitions = Math.Clamp(Environment.ProcessorCount, 1, 16);
        var perPartition = new int[partitions][];
        var counts = new int[partitions];
        var chunk = (samples + partitions - 1) / partitions;

        Parallel.For(0, partitions, p =>
        {
            var histogram = new int[HistogramBins];
            var counted = 0;

            var from = p * chunk;
            var to = Math.Min(from + chunk, samples);

            for (var s = from; s < to; s++)
            {
                rows.Rgb(s * stride, out var r, out var g, out var b);
                var luminance = (0.2126f * r) + (0.7152f * g) + (0.0722f * b);

                if (!float.IsFinite(luminance) || luminance <= 0f)
                    continue;

                var bin = (int)((MathF.Log2(luminance) - LogMin) * scale);
                histogram[Math.Clamp(bin, 0, HistogramBins - 1)]++;
                counted++;
            }

            perPartition[p] = histogram;
            counts[p] = counted;
        });

        var merged = new int[HistogramBins];
        var total = 0L;

        for (var p = 0; p < partitions; p++)
        {
            total += counts[p];
            var histogram = perPartition[p];

            for (var bin = 0; bin < HistogramBins; bin++)
                merged[bin] += histogram[bin];
        }

        if (total == 0)
            return 1f;

        // Walk down from the top until 0.1% of samples have been passed.
        var budget = (long)(total * 0.001);
        var seen = 0L;

        for (var bin = HistogramBins - 1; bin >= 0; bin--)
        {
            seen += merged[bin];
            if (seen <= budget)
                continue;

            var white = MathF.Pow(2f, LogMin + ((bin + 1) / scale));

            // Never below 1.0: an image whose brightest pixel is dim is already display-ranged,
            // and pulling white down under it would blow it out instead of leaving it alone.
            return MathF.Max(white, 1f);
        }

        return 1f;
    }

    // ------------------------------------------------------------------
    //  Curves
    // ------------------------------------------------------------------

    private const int GammaLutSize = 65536;
    private static readonly byte[] GammaLut = BuildGammaLut();

    private static byte[] BuildGammaLut()
    {
        var lut = new byte[GammaLutSize];
        for (var i = 0; i < GammaLutSize; i++)
        {
            var t = i / (float)(GammaLutSize - 1);
            lut[i] = (byte)Math.Clamp(MathF.Round(SrgbEncode(t) * 255f), 0f, 255f);
        }

        return lut;
    }
    
    private static float SrgbEncode(float linear)
        => linear <= 0.0031308f
            ? linear * 12.92f
            : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;

    /// Rec.709 luminance weights - the primaries the tone-mapped bitmap is tagged with.
    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    /// <summary>
    /// Curves one pixel, keeping its color: luminance goes through the curve and all three
    /// channels are scaled by the same factor.
    /// </summary>
    private static void ToneMapPixel(float r, float g, float b, ToneMapMode mode, float whitePoint, float exposureScale, float acesScale, out byte outR, out byte outG, out byte outB)
    {
        if (mode == ToneMapMode.Clip)
        {
            outR = ToneMap(r, mode, whitePoint, exposureScale);
            outG = ToneMap(g, mode, whitePoint, exposureScale);
            outB = ToneMap(b, mode, whitePoint, exposureScale);
            return;
        }

        var linearR = Sanitize(r) * exposureScale;
        var linearG = Sanitize(g) * exposureScale;
        var linearB = Sanitize(b) * exposureScale;

        var luminance = (LumaR * linearR) + (LumaG * linearG) + (LumaB * linearB);
        if (luminance <= 1e-5f)
        {
            outR = outG = outB = 0;
            return;
        }

        // Real EXRs hold values that overflow the squared terms in both curves, and inf/inf is
        // NaN - which as a scale would take the whole pixel to black. Every curve has flattened
        // well before this point anyway.
        if (luminance > 1e18f)
        {
            outR = outG = outB = GammaLut[GammaLutSize - 1];
            return;
        }

        var target = mode == ToneMapMode.ReinhardExtended
            ? ReinhardExtended(luminance, whitePoint)
            : Aces(luminance) * acesScale;

        var scale = target / luminance;
        var mappedR = linearR * scale;
        var mappedG = linearG * scale;
        var mappedB = linearB * scale;

        // Spend saturation on whatever will not fit, by pulling toward the neutral of the same
        // luminance.
        var peak = MathF.Max(mappedR, MathF.Max(mappedG, mappedB));
        if (peak > 1f)
        {
            var t = Math.Clamp((peak - 1f) / MathF.Max(peak - target, 1e-5f), 0f, 1f);
            mappedR += (target - mappedR) * t;
            mappedG += (target - mappedG) * t;
            mappedB += (target - mappedB) * t;
        }

        outR = Encode(mappedR);
        outG = Encode(mappedG);
        outB = Encode(mappedB);
    }

    private static float Sanitize(float value) => float.IsNaN(value) ? 0f : MathF.Max(value, 0f);

    internal static byte Encode(float mapped) => GammaLut[(int)((Math.Clamp(mapped, 0f, 1f) * (GammaLutSize - 1)) + 0.5f)];

    /// <summary>Applies the selected curve then the sRGB encode. NaN maps to 0 (black).</summary>
    private static byte ToneMap(float value, ToneMapMode mode, float whitePoint, float exposureScale)
    {
        if (float.IsNaN(value))
            return 0;

        var x = MathF.Max(value, 0f) * exposureScale;

        // Guard every curve, not just one: the huge values real EXRs carry overflow the squared
        // terms in both ACES and Reinhard to infinity on both sides of their divisions, and
        // inf/inf is NaN - which would come out black, the exact opposite of what the brightest
        // pixel in the image should be. Above this point every curve has flattened anyway.
        if (x > 1e18f)
            return GammaLut[GammaLutSize - 1];

        var mapped = mode switch
        {
            ToneMapMode.ReinhardExtended => ReinhardExtended(x, whitePoint),
            ToneMapMode.Clip => x,
            _ => Aces(x)
        };

        mapped = Math.Clamp(mapped, 0f, 1f);

        return GammaLut[(int)((mapped * (GammaLutSize - 1)) + 0.5f)];
    }

    /// <summary>ACES filmic tone curve (Narkowicz 2015).</summary>
    internal static float Aces(float x)
    {
        const float a = 2.51f;
        const float b = 0.03f;
        const float c = 2.43f;
        const float d = 0.59f;
        const float e = 0.14f;

        return (x * ((a * x) + b)) / ((x * ((c * x) + d)) + e);
    }

    /// <summary>
    /// Reinhard extended: compresses the whole range while mapping <paramref name="whitePoint"/>
    /// to 1.0, so the brightest content stays separated from its surroundings instead of sharing
    /// white with everything above the curve's knee.
    /// </summary>
    private static float ReinhardExtended(float x, float whitePoint)
    {
        var w2 = whitePoint * whitePoint;
        return x * (1f + (x / w2)) / (1f + x);
    }

    /// <summary>Alpha to 8 bits - NOT gamma-encoded. NaN is treated as fully opaque.</summary>
    internal static byte AlphaToByte(float value)
    {
        if (float.IsNaN(value))
            return 255;

        return (byte)Math.Clamp(value * 255f, 0f, 255f);
    }
}
