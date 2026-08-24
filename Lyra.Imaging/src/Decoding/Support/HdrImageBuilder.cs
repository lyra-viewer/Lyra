using Lyra.Common;
using Lyra.Imaging.Content;
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
    /// Above this many pixels, keep the 8-bit tone-mapped form instead of scene-referred half-float.
    ///
    /// Half-float costs 8 bytes per pixel against 4, which is a fair price for live exposure and
    /// curve controls at 4K (64 MB) or 6K (183 MB), and a bad one at 16K (1 GB) - precisely the
    /// sizes where the controls are least usable because the image barely fits anyway. 32
    /// megapixels puts the boundary above every common HDR panorama and below the 16K plates.
    /// </summary>
    private const long LivePixelBudget = 32L * 1024 * 1024;

    /// <summary>
    /// How much one image may spend on scene-referred tiles, as a share of the decoded-image cache.
    /// </summary>
    private const int SceneTileBudgetShare = 4;

    private static long TiledSceneBudget => ImageLoader.CacheBudgetBytes / SceneTileBudgetShare;

    /// <summary>
    /// Builds HDR content from interleaved RGBA float pixels. <paramref name="isGrayscale"/>
    /// reports whether every pixel has R == G == B, which decoders publish as metadata.
    /// </summary>
    public static ICompositeContent Build(Span<float> rgba, int width, int height, Composite composite, CancellationToken ct, out bool isGrayscale)
    {
        var pixels = (long)width * height;
        if (pixels <= LivePixelBudget)
        {
            var single = TryBuildHalfFloat(rgba, width, height, ct, out isGrayscale);
            if (single is not null)
            {
                composite.HdrBakedReason = null;
                return new HdrRasterContent(single, SKImage.FromBitmap(single), HdrToneMap.MeasureWhitePoint(rgba));
            }

            Logger.Warning($"[HdrImageBuilder] {width}x{height} does not fit as half-float; falling back to a tone-mapped 8-bit decode (HDR controls will not apply).");

            composite.HdrBakedReason = "Half-float allocation failed.";
            return BuildToneMapped(rgba, width, height, composite, ct, out isGrayscale);
        }
        
        var halfFloatBytes = pixels * 8;
        if (halfFloatBytes <= TiledSceneBudget)
        {
            var tiled = TryBuildHalfFloat(rgba, width, height, ct, out isGrayscale);
            if (tiled is not null)
            {
                Logger.Info($"[HdrImageBuilder] {width}x{height} is {pixels / 1024 / 1024} MP, over the " +
                            $"{LivePixelBudget / 1024 / 1024} MP single-texture budget but within the " +
                            $"{TiledSceneBudget / 1024 / 1024} MB scene-referred tile budget " +
                            $"({halfFloatBytes / 1024 / 1024} MB); keeping the whole image as light.");

                composite.HdrBakedReason = null;
                return BuildSceneReferredTiles(tiled, composite, HdrToneMap.MeasureWhitePoint(rgba));
            }
        }
        
        Logger.Info($"[HdrImageBuilder] {width}x{height} is {pixels / 1024 / 1024} MP " +
                    $"({halfFloatBytes / 1024 / 1024} MB as half-float), over the " +
                    $"{TiledSceneBudget / 1024 / 1024} MB scene-referred tile budget; tone-mapping at " +
                    "decode instead (halves the footprint; HDR controls apply to the preview only).");

        composite.HdrBakedReason = $"{halfFloatBytes / 1024 / 1024} MB over the " +
                                   $"{TiledSceneBudget / 1024 / 1024} MB scene budget.";

        return BuildToneMapped(rgba, width, height, composite, ct, out isGrayscale);
    }

    /// <summary>
    /// Hands a half-float image to the tiled builder and marks what comes back as scene-referred,
    /// so preview and tiles alike are tone-mapped at draw time.
    /// </summary>
    private static ICompositeContent BuildSceneReferredTiles(SKBitmap halfFloat, Composite composite, float whitePoint)
    {
        var content = RasterContentBuilder.Build(halfFloat, composite);
        if (content is RasterLargeContent large)
            large.MarkSceneReferred(whitePoint);

        return content;
    }

    /// <summary>
    /// Converts the source light into a half-float image, or null when it will not fit in memory.
    /// </summary>
    private static SKBitmap? TryBuildHalfFloat(Span<float> rgba, int width, int height, CancellationToken ct, out bool isGrayscale)
    {
        isGrayscale = true;

        // Linear, because the values are linear - tagging them sRGB would have Skia apply a
        // transfer function to data that has not been encoded with one.
        var info = new SKImageInfo(width, height, SKColorType.RgbaF16, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());

        var bitmap = TryAllocate(info);
        if (bitmap is null)
            return null;

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
    
    private static ICompositeContent BuildToneMapped(Span<float> rgba, int width, int height, Composite composite, CancellationToken ct, out bool isGrayscale)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
        var bitmap = new SKBitmap(info);

        HdrToneMap.ToBitmap(rgba, bitmap, ct, out isGrayscale);

        // Large by definition at this point, so the builder decides between one texture and tiles.
        var content = RasterContentBuilder.Build(bitmap, composite);
        if (content is RasterLargeContent large)
            AttachScenePreview(large, rgba, width, height, ct);

        return content;
    }

    /// <summary>
    /// Replaces the large form's preview with scene-referred half-float light, so an image whose
    /// full resolution must be baked still uses the display's headroom at fit-to-window.
    /// </summary>
    /// <remarks>
    /// About 130 MB for a 128 MP panorama. On failure the tone-mapped preview stands.
    /// </remarks>
    private static void AttachScenePreview(RasterLargeContent large, Span<float> rgba, int width, int height, CancellationToken ct)
    {
        try
        {
            var (targetWidth, targetHeight) = RasterContentBuilder.PreviewSize(width, height);

            var preview = DownsampleToHalfFloat(rgba, width, height, targetWidth, targetHeight, ct);
            if (preview is null)
                return;

            large.SetScenePreview(preview, HdrToneMap.MeasureWhitePoint(rgba));

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
    /// Box-averages the float pixels straight into a half-float image of the requested size.
    /// </summary>
    internal static SKImage? DownsampleToHalfFloat(Span<float> rgba, int width, int height, int targetWidth, int targetHeight, CancellationToken ct)
    {
        var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.RgbaF16, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear());

        var bitmap = TryAllocate(info);
        if (bitmap is null)
            return null;

        var xScale = width / (double)targetWidth;
        var yScale = height / (double)targetHeight;

        unsafe
        {
            fixed (float* srcPin = rgba)
            {
                var src = (nint)srcPin;
                var dst = (nint)bitmap.GetPixels();
                var dstRowBytes = bitmap.RowBytes;
                var options = new ParallelOptions { CancellationToken = ct };

                Parallel.For(0, targetHeight, options, ty =>
                {
                    var y0 = (int)(ty * yScale);
                    var y1 = Math.Max(y0 + 1, Math.Min(height, (int)((ty + 1) * yScale)));
                    var row = (Half*)((byte*)dst + ((nint)ty * dstRowBytes));

                    for (var tx = 0; tx < targetWidth; tx++)
                    {
                        var x0 = (int)(tx * xScale);
                        var x1 = Math.Max(x0 + 1, Math.Min(width, (int)((tx + 1) * xScale)));

                        double r = 0, g = 0, b = 0, a = 0;
                        var samples = 0;

                        for (var y = y0; y < y1; y++)
                        {
                            var line = (float*)src + ((nint)y * width * 4);

                            for (var x = x0; x < x1; x++)
                            {
                                var i = x * 4;
                                r += line[i];
                                g += line[i + 1];
                                b += line[i + 2];
                                a += line[i + 3];
                                samples++;
                            }
                        }

                        var scale = samples > 0 ? 1.0 / samples : 0.0;
                        var o = tx * 4;

                        row[o + 0] = (Half)(r * scale);
                        row[o + 1] = (Half)(g * scale);
                        row[o + 2] = (Half)(b * scale);
                        row[o + 3] = (Half)(a * scale);
                    }
                });
            }
        }

        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
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