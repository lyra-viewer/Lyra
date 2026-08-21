using Lyra.Common;
using Lyra.Imaging.Content;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Turns linear scene-referred RGBA float pixels into <see cref="HdrRasterContent"/> - a
/// half-float image the renderer tone-maps at draw time, so exposure and curve changes never
/// touch the decoder again.
/// </summary>
internal static class HdrImageBuilder
{
    /// <summary>
    /// Above this many pixels, keep the 8-bit tone-mapped form instead of scene-referred half-float.
    ///
    /// Half-float costs 8 bytes per pixel against 4, which is a fair price for live exposure and
    /// curve controls at 4K (64 MB) or 6K (183 MB), and a bad one at 16K (1 GB) - precisely the
    /// sizes where the controls are least usable because the image barely fits anyway. 32
    /// megapixels puts the boundary above every common HDR panorama and below the 16K plates.
    /// </summary>
    private const long LivePixelBudget = 32L * 1024 * 1024;

    /// <summary>
    /// Builds HDR content from interleaved RGBA float pixels. <paramref name="isGrayscale"/>
    /// reports whether every pixel has R == G == B, the same fact the tone mapper used to
    /// return, since decoders publish it as metadata.
    /// </summary>
    public static ICompositeContent Build(Span<float> rgba, int width, int height, Composite composite, CancellationToken ct, out bool isGrayscale)
    {
        // Linear, because the values are linear - tagging them sRGB would have Skia apply a
        // transfer function to data that has not been encoded with one.
        var info = new SKImageInfo(width, height, SKColorType.RgbaF16, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());

        // F16 costs 8 bytes per pixel against 4 for the tone-mapped 8-bit form, and on very large
        // images that is the difference between fitting in memory and not: a 24K environment map
        // needs 2.4 GB this way. Losing the live controls on such a file is a far better outcome
        // than failing to open it, so fall back to baking the curve in.
        var pixels = (long)width * height;
        if (pixels > LivePixelBudget)
        {
            Logger.Info($"[HdrImageBuilder] {width}x{height} is {pixels / 1024 / 1024} MP, over the " +
                        $"{LivePixelBudget / 1024 / 1024} MP live budget; tone-mapping at decode instead " +
                        "(halves the footprint; HDR controls will not apply).");

            return BuildToneMapped(rgba, width, height, composite, ct, out isGrayscale);
        }

        var bitmap = TryAllocate(info);
        if (bitmap is null)
        {
            Logger.Warning($"[HdrImageBuilder] {width}x{height} does not fit as half-float; " +
                           $"falling back to a tone-mapped 8-bit decode (HDR controls will not apply).");
            
            return BuildToneMapped(rgba, width, height, composite, ct, out isGrayscale);
        }

        var whitePoint = HdrToneMap.MeasureWhitePoint(rgba);

        var gray = true;

        unsafe
        {
            fixed (float* srcPin = rgba)
            {
                var src = (nint)srcPin;
                var dst = (nint)bitmap.GetPixels();
                var dstRowBytes = bitmap.RowBytes;
                var options = new ParallelOptions { CancellationToken = ct };

                Parallel.For(0, height, options, y =>
                {
                    if (!ConvertRow((float*)src, (byte*)dst, y, width, dstRowBytes))
                        gray = false;
                });
            }
        }

        isGrayscale = gray;

        bitmap.SetImmutable();
        return new HdrRasterContent(bitmap, SKImage.FromBitmap(bitmap), whitePoint);
    }

    private static SKBitmap? TryAllocate(SKImageInfo info)
    {
        try
        {
            var bitmap = new SKBitmap(info);
            return bitmap.GetPixels() == IntPtr.Zero ? null : bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }
    
    private static ICompositeContent BuildToneMapped(Span<float> rgba, int width, int height, Composite composite, CancellationToken ct, out bool isGrayscale)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);

        HdrToneMap.ToBitmap(rgba, bitmap, ct, out isGrayscale);

        // Past the live budget the image is large by definition, so this is exactly where a single
        // texture stops being viable - hand it to the builder rather than wrapping it directly.
        return RasterContentBuilder.Build(bitmap, composite);
    }

    private static unsafe bool ConvertRow(float* src, byte* dst, int y, int width, int dstRowBytes)
    {
        var srcRow = src + ((nint)y * width * 4);
        var dstRow = (Half*)(dst + ((nint)y * dstRowBytes));
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

            // NaN would propagate through the shader into a black pixel and negatives are not
            // meaningful light, so both are cleaned up once here rather than in every curve.
            dstRow[idx + 0] = ToHalf(r);
            dstRow[idx + 1] = ToHalf(g);
            dstRow[idx + 2] = ToHalf(b);
            dstRow[idx + 3] = float.IsNaN(a) ? (Half)1f : (Half)Math.Clamp(a, 0f, 1f);
        }

        return gray;
    }

    /// <summary>
    /// Clamps into what half can represent. Half tops out at 65504, and real EXRs carry values
    /// past that as well as infinities; letting either through would store +Inf, which the
    /// shader's rational curves turn into NaN and then black - the opposite of a bright pixel.
    /// </summary>
    private static Half ToHalf(float value)
    {
        if (float.IsNaN(value))
            return (Half)0f;

        return (Half)MathF.Min(MathF.Max(value, 0f), 65504f);
    }
}