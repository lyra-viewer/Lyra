using System.Buffers;
using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Hdr;
using Lyra.Imaging.Loading;
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
    /// Above this many pixels the image is tiled: half-float within <see cref="TiledSceneBudget"/>,
    /// <see cref="SceneEncoding.Compressed"/> light past it, at 4 bytes per pixel instead of 8.
    /// </summary>
    private const long LivePixelBudget = 32L * 1024 * 1024;

    /// <summary>
    /// How much one image may spend on scene-referred tiles, as a share of the decoded-image cache.
    /// </summary>
    private const int SceneTileBudgetShare = 4;

    private static long TiledSceneBudget => ImageLoader.CacheBudgetBytes / SceneTileBudgetShare;

    /// <summary>
    /// Builds HDR content from interleaved RGBA float pixels. <paramref name="facts"/> says whether
    /// every pixel has R == G == B and how far above SDR white they reach, for the caller to publish.
    /// </summary>
    public static unsafe ICompositeContent Build(Span<float> rgba, int width, int height, Composite composite, CancellationToken ct, out PixelFacts facts)
    {
        if (rgba.Length < (long)width * height * 4)
            throw new ArgumentException("Fewer pixels than the dimensions say.", nameof(rgba));

        fixed (float* pin = rgba)
            return Build(new FloatRgbaRows(pin, width, height), composite, ct, out facts);
    }

    /// <summary>The same, from linear rows of any layout, carrying what is known of where the light ends.</summary>
    public static ICompositeContent Build(LinearRows rows, Composite composite, CancellationToken ct, out PixelFacts facts, SceneRange range = default) =>
        Build(rows, composite, ct, out facts, range, new HdrBudgets(LivePixelBudget, TiledSceneBudget));

    /// <summary>The same, with the size thresholds given, so tests need not allocate gigabytes to reach them.</summary>
    internal static ICompositeContent Build(LinearRows rows, Composite composite, CancellationToken ct, out PixelFacts facts, SceneRange range, HdrBudgets budgets)
    {
        var whitePoint = HdrToneMap.MeasureWhitePoint(rows);
        var content = Build(rows, whitePoint, range, budgets, composite, ct, out var isGrayscale);

        switch (content)
        {
            case HdrRasterContent hdr:
                hdr.Range = range;
                break;
            case RasterLargeContent large:
                large.Range = range;
                break;
        }

        facts = new PixelFacts(isGrayscale, DescribeDynamicRange(whitePoint));
        return content;
    }

    private static ICompositeContent Build(LinearRows rows, float whitePoint, SceneRange range, HdrBudgets budgets, Composite composite, CancellationToken ct, out bool isGrayscale)
    {
        var width = rows.Width;
        var height = rows.Height;
        var pixels = rows.PixelCount;
        if (pixels <= budgets.LivePixels)
        {
            var single = TryBuildHalfFloat(rows, ct, out isGrayscale);
            if (single is not null)
            {
                composite.HdrBakedReason = null;
                return new HdrRasterContent(single, SKImage.FromBitmap(single), whitePoint);
            }

            Logger.Warning($"[HdrImageBuilder] {width}x{height} does not fit as half-float; falling back to a tone-mapped 8-bit decode (HDR controls will not apply).");

            composite.HdrBakedReason = "Half-float allocation failed.";
            return BuildToneMapped(rows, composite, whitePoint, range, ct, out isGrayscale);
        }

        var halfFloatBytes = pixels * 8;
        if (halfFloatBytes <= budgets.TiledSceneBytes)
        {
            var tiled = TryBuildHalfFloat(rows, ct, out isGrayscale);
            if (tiled is not null)
            {
                Logger.Debug($"[HdrImageBuilder] {width}x{height} is {pixels / 1024 / 1024} MP, over the " +
                             $"{budgets.LivePixels / 1024 / 1024} MP live-controls budget but within the " +
                             $"{budgets.TiledSceneBytes / 1024 / 1024} MB scene-referred budget " +
                             $"({halfFloatBytes / 1024 / 1024} MB); keeping the whole image as light.");

                composite.HdrBakedReason = null;
                return RasterContentBuilder.Build(tiled, composite, whitePoint);
            }
        }

        Logger.Debug($"[HdrImageBuilder] {width}x{height} is {pixels / 1024 / 1024} MP " +
                     $"({halfFloatBytes / 1024 / 1024} MB as half-float), over the " +
                     $"{budgets.TiledSceneBytes / 1024 / 1024} MB scene-referred tile budget; keeping the " +
                     "tiles as 8-bit compressed light instead (halves the footprint, controls stay live).");

        composite.HdrBakedReason = null;

        return BuildCompressed(rows, composite, whitePoint, ct, out isGrayscale);
    }

    private static string DescribeDynamicRange(float whitePoint)
    {
        // The white point is a histogram bin's upper edge, so clipped content lands a hair above 1.0.
        if (MathF.Log2(whitePoint) < 0.05f)
            return "Within SDR white";

        var multiplier = whitePoint < 100f ? whitePoint.ToString("0.0") : whitePoint.ToString("0");

        return $"{multiplier}x SDR white ({MathF.Log2(whitePoint):0.0} stops)";
    }

    /// <summary>
    /// Converts the source light into a half-float image, or null when it will not fit in memory.
    /// </summary>
    private static unsafe SKBitmap? TryBuildHalfFloat(LinearRows rows, CancellationToken ct, out bool isGrayscale)
    {
        isGrayscale = true;

        // Linear, because the values are linear - tagging them sRGB would have Skia apply a
        // transfer function to data that has not been encoded with one.
        var info = new SKImageInfo(rows.Width, rows.Height, SKColorType.RgbaF16, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());

        var bitmap = TryAllocate(info);
        if (bitmap is null)
            return null;

        try
        {
            var dst = (nint)bitmap.GetPixels();
            var dstRowBytes = bitmap.RowBytes;
            var width = rows.Width;

            isGrayscale = rows.AllRows(ct, (y, row) => ConvertRow(row, (Half*)((byte*)dst + (nint)y * dstRowBytes), width));
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        bitmap.SetImmutable();

        return bitmap;
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

    private static ICompositeContent BuildToneMapped(LinearRows rows, Composite composite, float whitePoint, SceneRange range, CancellationToken ct, out bool isGrayscale)
    {
        var info = new SKImageInfo(rows.Width, rows.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
        var bitmap = new SKBitmap(info);

        try
        {
            HdrToneMap.ToBitmap(rows, bitmap, whitePoint, range, ct, out isGrayscale);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        // Large by definition at this point, so the builder decides between one texture and tiles.
        var content = RasterContentBuilder.Build(bitmap, composite);
        if (content is RasterLargeContent large)
            AttachScenePreview(large, rows, whitePoint, ct);

        return content;
    }

    private static ICompositeContent BuildCompressed(LinearRows rows, Composite composite, float whitePoint, CancellationToken ct, out bool isGrayscale)
    {
        var info = new SKImageInfo(rows.Width, rows.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
        var bitmap = new SKBitmap(info);

        try
        {
            isGrayscale = CompressedRows.Write(rows, bitmap, ct);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        var content = RasterContentBuilder.Build(bitmap, composite, whitePoint, SceneEncoding.Compressed);
        if (content is RasterLargeContent large)
            AttachScenePreview(large, rows, whitePoint, ct);

        return content;
    }

    /// <summary>
    /// Replaces the large form's preview with scene-referred half-float light, so an image whose
    /// full resolution cannot be half-float still uses the display's headroom at fit-to-window.
    /// </summary>
    /// <remarks>
    /// About 130 MB for a 128 MP panorama. On failure the tone-mapped preview stands.
    /// </remarks>
    private static void AttachScenePreview(RasterLargeContent large, LinearRows rows, float whitePoint, CancellationToken ct)
    {
        try
        {
            var (targetWidth, targetHeight) = RasterContentBuilder.PreviewSize(rows.Width, rows.Height);

            var preview = DownsampleToHalfFloat(rows, targetWidth, targetHeight, ct);
            if (preview is null)
                return;

            large.SetScenePreview(preview, whitePoint);

            Logger.Info($"[HdrImageBuilder] Preview kept scene-referred at {targetWidth}x{targetHeight} " +
                        $"({(long)targetWidth * targetHeight * 8 / 1024 / 1024} MB), so the display's headroom " +
                        "still applies at fit-to-window.");
        }
        catch (Exception ex)
        {
            Logger.Warning($"[HdrImageBuilder] Could not build a scene-referred preview ({ex.Message}); the tone-mapped one stands.");
        }
    }

    /// <summary>
    /// Box-averages the light straight into a half-float image of the requested size.
    /// </summary>
    internal static SKImage? DownsampleToHalfFloat(LinearRows rows, int targetWidth, int targetHeight, CancellationToken ct)
    {
        var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.RgbaF16, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());

        var bitmap = TryAllocate(info);
        if (bitmap is null)
            return null;

        try
        {
            Downsample(rows, targetWidth, targetHeight, bitmap, ct);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }

    private static unsafe void Downsample(LinearRows rows, int targetWidth, int targetHeight, SKBitmap bitmap, CancellationToken ct)
    {
        var width = rows.Width;
        var height = rows.Height;
        var xScale = width / (double)targetWidth;
        var yScale = height / (double)targetHeight;

        var dst = (nint)bitmap.GetPixels();
        var dstRowBytes = bitmap.RowBytes;

        var x0 = new int[targetWidth];
        var x1 = new int[targetWidth];
        for (var tx = 0; tx < targetWidth; tx++)
        {
            x0[tx] = (int)(tx * xScale);
            x1[tx] = Math.Max(x0[tx] + 1, Math.Min(width, (int)((tx + 1) * xScale)));
        }

        rows.AllOf(targetHeight, ct, (ty, scratch) =>
        {
            var y0 = (int)(ty * yScale);
            var y1 = Math.Max(y0 + 1, Math.Min(height, (int)((ty + 1) * yScale)));
            var sums = ArrayPool<double>.Shared.Rent(targetWidth * 4);
            Array.Clear(sums, 0, targetWidth * 4);

            for (var y = y0; y < y1; y++)
            {
                var line = rows.Row(y, scratch);

                for (var tx = 0; tx < targetWidth; tx++)
                {
                    double r = 0, g = 0, b = 0, a = 0;

                    for (var x = x0[tx]; x < x1[tx]; x++)
                    {
                        var i = x * 4;
                        r += Light(line[i]);
                        g += Light(line[i + 1]);
                        b += Light(line[i + 2]);
                        a += float.IsNaN(line[i + 3]) ? 1f : Math.Clamp(line[i + 3], 0f, 1f);
                    }

                    var o = tx * 4;
                    sums[o] += r;
                    sums[o + 1] += g;
                    sums[o + 2] += b;
                    sums[o + 3] += a;
                }
            }

            var row = (Half*)((byte*)dst + (nint)ty * dstRowBytes);

            for (var tx = 0; tx < targetWidth; tx++)
            {
                var samples = (y1 - y0) * (x1[tx] - x0[tx]);
                var scale = samples > 0 ? 1.0 / samples : 0.0;
                var o = tx * 4;

                // A sun averages past what half holds; +Inf would come out of the shader black.
                row[o + 0] = ToHalf((float)(sums[o] * scale));
                row[o + 1] = ToHalf((float)(sums[o + 1] * scale));
                row[o + 2] = ToHalf((float)(sums[o + 2] * scale));
                row[o + 3] = (Half)(sums[o + 3] * scale);
            }

            ArrayPool<double>.Shared.Return(sums);
            return true;
        });
    }

    private static float Light(float value) => value > 0f ? value : 0f;

    private static unsafe bool ConvertRow(float* srcRow, Half* dstRow, int width)
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

internal readonly record struct HdrBudgets(long LivePixels, long TiledSceneBytes);