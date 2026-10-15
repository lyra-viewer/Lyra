using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Raw;

/// <summary>The gain that brings a developed raw to the camera's own brightness.</summary>
internal static class RawExposure
{
    /// <summary>For a raw without a preview to match: the middle of what matching gives across cameras.</summary>
    public const float FallbackStops = 0.3f;

    public const float MinStops = -1.5f;
    public const float MaxStops = 3f;

    /// <summary>How far above SDR white raw highlights may reach: about what an iPhone HDR photo spends.</summary>
    public const float Peak = 4f;

    /// <summary>A median is stable long before this many samples.</summary>
    private const int SampleBudget = 1 << 16;

    /// <summary>What the content says about its light once developed with <paramref name="stops"/> of gain.</summary>
    public static SceneRange RangeFor(float stops) => new(SceneWhite: MathF.Pow(2f, stops), Peak: Peak);

    /// <summary>
    /// The stops of gain that put <paramref name="rawMedian"/> (linear, saturation at 1.0) where the
    /// camera put <paramref name="previewMedian"/> (linear display luminance), or null when either
    /// is too dark to say anything.
    /// </summary>
    public static float? MatchStops(float previewMedian, float rawMedian)
    {
        if (!(previewMedian > 1e-4f) || !(rawMedian > 1e-5f) || !float.IsFinite(previewMedian) || !float.IsFinite(rawMedian))
            return null;

        if (Displayed(MinStops, rawMedian) >= previewMedian)
            return MinStops;

        if (Displayed(MaxStops, rawMedian) <= previewMedian)
            return MaxStops;

        // Rises with the gain for any median below saturation, so bisection finds the one answer.
        float low = MinStops, high = MaxStops;
        for (var i = 0; i < 40; i++)
        {
            var middle = (low + high) / 2f;
            if (Displayed(middle, rawMedian) < previewMedian)
                low = middle;
            else
                high = middle;
        }

        return (low + high) / 2f;
    }

    /// <summary>Where the default curve puts <paramref name="median"/> after <paramref name="stops"/> of gain.</summary>
    internal static float Displayed(float stops, float median)
    {
        var gain = MathF.Pow(2f, stops);
        return HdrToneMap.Aces(gain * median) / HdrToneMap.Aces(gain);
    }

    /// <summary>The median luminance of a display-referred preview, as linear light in sRGB primaries.</summary>
    public static unsafe float? MedianLuminance(SKBitmap preview)
    {
        if (preview.Width <= 0 || preview.Height <= 0)
            return null;

        // Skia does the color management: an Adobe RGB preview reads back as the light it encodes.
        var info = new SKImageInfo(preview.Width, preview.Height, SKColorType.RgbaF32, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());
        var pixels = new float[preview.Width * preview.Height * 4];

        fixed (float* p = pixels)
        {
            using var image = SKImage.FromBitmap(preview);
            if (!image.ReadPixels(info, (IntPtr)p, info.RowBytes, 0, 0))
                return null;

            var rows = new FloatRgbaRows(p, preview.Width, preview.Height);
            return Median(rows);
        }
    }

    /// <summary>The median luminance of developed light, with saturation at 1.0.</summary>
    public static float? MedianLuminance(LinearRows rows) => rows.PixelCount <= 0 ? null : Median(rows);

    private static float Median(LinearRows rows)
    {
        var pixels = rows.PixelCount;
        var stride = Math.Max(1, pixels / SampleBudget);
        var samples = new float[(int)((pixels + stride - 1) / stride)];

        for (var i = 0; i < samples.Length; i++)
        {
            rows.Rgb(i * stride, out var r, out var g, out var b);
            samples[i] = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        Array.Sort(samples);
        return samples[samples.Length / 2];
    }
}