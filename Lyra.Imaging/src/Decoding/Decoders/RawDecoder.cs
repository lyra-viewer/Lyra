using System.Diagnostics;
using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders.Raw;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Loading;
using Lyra.Imaging.Metadata;
using Lyra.ManagedCodecs.Raster.Dng;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

/// <summary>
/// Camera raw through LibRaw: demosaiced to linear light with the camera's white balance and kept
/// scene-referred, so exposure, the curve and EDR headroom apply as they do to EXR. The developed
/// image and the camera's embedded previews are variants of one another; the developed one is
/// shown, with the best preview standing in for it until it is ready.
/// </summary>
internal sealed class RawDecoder : DecoderBase, IThumbnailDecoder
{
    public override bool CanDecode(ImageFormatType format) => format == ImageFormatType.Raw;

    /// <summary>How far the preview's shape may stray from the raw's before it is not shown in its place.</summary>
    private const double PreviewAspectTolerance = 0.03;

    private const string StageLabel = "Raw";

    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        composite.Structure = DescribeStructure(path);

        if (TryShowPreview(composite, path, ct) is not { } renditions)
        {
            var developed = DevelopFile(composite, path, warning => composite.Warning = warning, ct);
            composite.Content = RawRenditions.Around(composite, path, developed);
            composite.Stage = Developed(clock);
            return;
        }

        clock.Restart();

        try
        {
            composite.BackgroundDecodeTask = Task.Factory.StartNew(() => DevelopBehindPreview(composite, path, renditions, clock, ct),
                CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Current);
        }
        catch (InvalidOperationException)
        {
            DevelopBehindPreview(composite, path, renditions, clock, ct);
        }
    }

    private static IReadOnlyList<StructureGroup>? DescribeStructure(string path)
    {
        try
        {
            using var stream = DecoderIO.OpenRandomAccessRead(path);
            return RawStructure.Describe(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static LoadStage Developed(Stopwatch clock) => new(StageLabel, $"Developed in {clock.Elapsed.TotalSeconds:0.0} s");
    
    private static VariantRasterContent? TryShowPreview(Composite composite, string path, CancellationToken ct)
    {
        using var image = RawNative.OpenFile(path, out var info);
        if (image is null || info.Width <= 0 || info.Height <= 0)
            return null;

        var (maxWidth, maxHeight) = RasterContentBuilder.PreviewBounds();

        SKBitmap? preview;
        try
        {
            preview = DecodePreview(image, info, Math.Max(maxWidth, maxHeight), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Logger.Warning(typeof(RawDecoder), $"The embedded preview of {path} could not be decoded: {e.Message}");
            return null;
        }

        if (preview is null)
            return null;

        var previewAspect = preview.Width / (double)preview.Height;
        var rawAspect = info.Width / (double)info.Height;
        if (Math.Abs(previewAspect - rawAspect) > rawAspect * PreviewAspectTolerance)
        {
            Logger.Debug(typeof(RawDecoder), $"Not showing the {preview.Width}x{preview.Height} preview of {path} for a {info.Width}x{info.Height} image.");
            preview.Dispose();
            return null;
        }

        try
        {
            DecoderValidation.RequireSaneDimensions(info.Width, info.Height, bytesPerPixel: 8);
            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            preview.Dispose();
            throw;
        }

        composite.FullWidth = info.Width;
        composite.FullHeight = info.Height;
        composite.ReportPixelCount(info.Width, info.Height);
        composite.AppliedOrientation = OrientationOf(info.Flip);
        Describe(composite, info);

        preview.SetImmutable();
        var standIn = new RasterContent(preview, SKImage.FromBitmap(preview));

        VariantRasterContent renditions;
        try
        {
            renditions = RawRenditions.WithStandIn(composite, path, image, info, standIn);
        }
        catch
        {
            standIn.Dispose();
            throw;
        }

        composite.Content = renditions;
        composite.Stage = new LoadStage(StageLabel, "Camera preview developing...");
        composite.HdrPending = true;
        composite.SignalReady();

        return renditions;
    }

    /// <summary>
    /// Develops the raw data and puts it in place of the stand-in. On failure the preview stays,
    /// marked as all there is; completion is signaled either way unless the load was canceled.
    /// </summary>
    private static void DevelopBehindPreview(Composite composite, string path, VariantRasterContent renditions, Stopwatch clock, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var developed = DevelopFile(composite, path, warning => renditions.RecordWarning(RawRenditions.Developed, warning), ct);
            renditions.ReplaceVariant(RawRenditions.Developed, developed.Content, RawRenditions.DescribeDeveloped(composite, developed));
            composite.Stage = Developed(clock);
        }
        catch (OperationCanceledException)
        {
            composite.State = CompositeState.Cancelled;
            return;
        }
        catch (Exception e)
        {
            Logger.Warning(typeof(RawDecoder), $"Showing only the embedded preview of {path}: {e.Message}");
            renditions.RecordWarning(RawRenditions.Developed, LoadWarning.PartiallyDecoded($"The raw data could not be developed, so this is the camera's preview ({e.Message})"));
            composite.Stage = null;
        }
        finally
        {
            composite.HdrPending = false;
        }

        composite.SignalComplete();
    }

    internal readonly record struct DevelopedImage(ICompositeContent Content, int Width, int Height);
    
    internal static DevelopedImage DevelopFile(Composite composite, string path, Action<LoadWarning> warn, CancellationToken ct)
    {
        if (!NativeFileBuffer.ShouldBuffer(composite.FileSizeBytes ?? 0))
        {
            composite.ExifInfo = MetadataProcessor.ParseMetadata(path);
            composite.SignalProgress();

            using var opened = RawNative.OpenFile(path, out var fileInfo) ?? throw NativeErrors.DecodeFailed(RawNative.get_last_raw_error(), path);
            var fileProfile = ReadProfile(fileInfo, () => DecoderIO.OpenRandomAccessRead(path), path);

            return Develop(composite, opened, fileInfo, fileProfile, path, warn, ct);
        }

        using var data = NativeFileBuffer.Read(path, ct, out var readMs, composite.ReportTransferred);
        composite.CompleteTransfer((long)data.Length, readMs);
        ct.ThrowIfCancellationRequested();

        using (var stream = data.AsStream())
            composite.ExifInfo = MetadataProcessor.ParseMetadata(stream, path);

        // Behind a preview the image is already on screen, and the panel only redraws when told.
        composite.SignalProgress();

        // LibRaw reads the buffer in place, so it is disposed only after the handle.
        using var image = RawNative.OpenMemory(data.Data, data.Length, out var info) ?? throw NativeErrors.DecodeFailed(RawNative.get_last_raw_error(), path);
        var profile = ReadProfile(info, data.AsStream, path);

        return Develop(composite, image, info, profile, path, warn, ct);
    }

    /// <summary>The DNG's own tone curve and local tone map, when it carries them; null otherwise or on any trouble.</summary>
    private static DngRenderingProfile? ReadProfile(RawNative.RawInfo info, Func<Stream> open, string path)
    {
        if (info.DngVersion == 0)
            return null;

        try
        {
            using var stream = open();
            return DngRenderingProfile.Read(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Logger.Warning(typeof(RawDecoder), $"The DNG profile of {path} could not be read; developing without it. {e.Message}");
            return null;
        }
    }

    private static DevelopedImage Develop(Composite composite, RawNative.ImageHandle image, RawNative.RawInfo info, DngRenderingProfile? profile, string path, Action<LoadWarning> warn, CancellationToken ct)
    {
        DecoderValidation.RequireSaneDimensions(info.Width, info.Height, bytesPerPixel: 8);
        composite.ReportPixelCount(info.Width, info.Height);
        Describe(composite, info);

        ct.ThrowIfCancellationRequested();

        var previewMedian = PreviewMedian(image, info, ct);

        if (!TryDevelop(image, new RawNative.DevelopOptions { Demosaic = RawNative.DemosaicAhd }, ct, out var pixels, out var error))
        {
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"Failed to decode: {path}" : error);
        }

        try
        {
            DecoderValidation.RequireSaneDimensions(pixels.Width, pixels.Height, bytesPerPixel: 8);

            if (pixels.Width != info.Width || pixels.Height != info.Height)
                composite.ReportPixelCount(pixels.Width, pixels.Height);

            composite.FullWidth = pixels.Width;
            composite.FullHeight = pixels.Height;

            composite.AppliedOrientation = OrientationOf(info.Flip);
            ReportDevelopment(composite, pixels, path, warn);

            unsafe
            {
                var data = (ushort*)pixels.Data;
                var curve = profile?.ToneCurve;
                var map = profile?.GainTableMap;

                // The light as the file's maker meant it before any curve: its local tone map applied.
                LinearRows Shaped(float gain) => map is null
                    ? new Rgb16Rows(data, pixels.Width, pixels.Height, gain)
                    : new GainMappedRows(new Rgb16Rows(data, pixels.Width, pixels.Height), map, info.Flip, MathF.Pow(2f, info.BaselineExposure), gain);

                // A file that brings its own curve brings the exposure it was made for, too.
                var exposure = curve is not null
                    ? new Exposure(Math.Clamp(info.BaselineExposure, RawExposure.MinStops, RawExposure.MaxStops), "from the DNG")
                    : MatchedExposure(previewMedian, RawExposure.MedianLuminance(Shaped(1f)), info);

                DescribeRendering(composite, exposure, curve, map);

                var range = RawExposure.RangeFor(exposure.Stops) with { Curve = curve is null ? null : new CameraToneCurve(curve.Table) };

                ICompositeContent content;
                try
                {
                    content = HdrImageBuilder.Build(Shaped(MathF.Pow(2f, exposure.Stops)), composite, ct, out var facts, range);
                    facts.Describe(composite);
                }
                catch
                {
                    range.Curve?.Dispose();
                    throw;
                }

                return new DevelopedImage(content, pixels.Width, pixels.Height);
            }
        }
        finally
        {
            RawNative.free_raw_pixels(pixels.Owner);
        }
    }
    
    private static float? PreviewMedian(RawNative.ImageHandle image, RawNative.RawInfo info, CancellationToken ct)
    {
        try
        {
            using var preview = DecodePreview(image, info, PreviewSampleEdge, ct);
            return preview is null ? null : RawExposure.MedianLuminance(preview);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Logger.Debug(typeof(RawDecoder), $"No preview to match the exposure to: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Large enough to pass over the small thumbnails some cameras store next to the real preview -
    /// a CR2's uncompressed one is not the camera's rendering - and small enough to decode scaled.
    /// </summary>
    private const int PreviewSampleEdge = 1024;

    /// <summary>The gain the developed light gets, and where it came from, for FORMAT SPECIFIC.</summary>
    private readonly record struct Exposure(float Stops, string Source);

    private static Exposure MatchedExposure(float? previewMedian, float? rawMedian, RawNative.RawInfo info)
    {
        if (previewMedian is { } preview && rawMedian is { } raw && RawExposure.MatchStops(preview, raw) is { } matched)
            return new Exposure(matched, "matched to the camera's preview");

        return new Exposure(Math.Clamp(RawExposure.FallbackStops + info.BaselineExposure, RawExposure.MinStops, RawExposure.MaxStops), "default");
    }

    /// <summary>What developing found: damage as a warning on the developed rendition, the levels as a fact.</summary>
    private static void ReportDevelopment(Composite composite, RawNative.Pixels pixels, string path, Action<LoadWarning> warn)
    {
        if (pixels.Damage != RawNative.DamageNone)
        {
            var damage = pixels.Damage == RawNative.DamageTruncated
                ? "The file ends before the raw data does"
                : "The raw data is corrupt in places";

            Logger.Warning(typeof(RawDecoder), $"{damage}: {path}");
            warn(LoadWarning.PartiallyDecoded(damage));
        }

        if (pixels.WhiteLevel > 0)
        {
            var black = pixels.BlackLevelMin == pixels.BlackLevelMax ? $"{pixels.BlackLevelMin}" : $"{pixels.BlackLevelMin}-{pixels.BlackLevelMax}";
            composite.AddFormatSpecific("Black / White Level", $"{black} / {pixels.WhiteLevel}");
        }
    }

    private static void DescribeRendering(Composite composite, Exposure exposure, DngToneCurve? curve, DngGainTableMap? map)
    {
        composite.AddFormatSpecific("Exposure Baseline", $"{exposure.Stops:+0.0;-0.0;0.0} EV ({exposure.Source})");
        composite.AddFormatSpecific("Tone Curve", curve is null ? "Default" : $"The camera's own ({curve.PointCount} points)");

        if (map is not null)
            composite.AddFormatSpecific("Local Tone Map", $"{map.PointsV} x {map.PointsH} tables of {map.PointsN}");
    }
    
    private static bool TryDevelop(RawNative.ImageHandle image, RawNative.DevelopOptions options, CancellationToken ct, out RawNative.Pixels pixels, out string? error)
    {
        var preload = TaskScheduler.Current is PreloadTaskScheduler;

        var result = RawDevelopQueue.Shared.Run(() =>
        {
            var developed = TryDevelopNow(image, options, ct, out var developedPixels);
            return new DevelopResult(developed, developedPixels, developed ? null : NativeErrors.GetUtf8ZOrAnsiZ(RawNative.get_last_raw_error()));
        }, preload, ct);

        pixels = result.Pixels;
        error = result.Error;
        return result.Developed;
    }

    private readonly record struct DevelopResult(bool Developed, RawNative.Pixels Pixels, string? Error);

    private static bool TryDevelopNow(RawNative.ImageHandle image, RawNative.DevelopOptions options, CancellationToken ct, out RawNative.Pixels pixels)
    {
        // Disposed before the handle is, so the callback never sees a closed one.
        using var cancellation = ct.Register(() =>
        {
            try
            {
                RawNative.raw_cancel(image);
            }
            catch (ObjectDisposedException)
            {
            }
        });

        if (RawNative.raw_develop(image, options, out pixels) && pixels.Data != IntPtr.Zero)
            return true;

        if (pixels.Owner != IntPtr.Zero)
            RawNative.free_raw_pixels(pixels.Owner);

        pixels = default;
        return false;
    }

    private static void Describe(Composite composite, RawNative.RawInfo info)
    {
        var make = info.MakeText;
        var model = info.ModelText;
        var camera = model.StartsWith(make, StringComparison.OrdinalIgnoreCase) ? model : $"{make} {model}".Trim();
        if (camera.Length > 0)
            composite.AddFormatSpecific("Camera", camera);

        composite.AddFormatSpecific("Sensor", info.Sensor switch
        {
            RawNative.SensorBayer => info.CfaPatternText is { Length: > 0 } cfa ? $"Bayer ({cfa})" : "Bayer",
            RawNative.SensorXTrans => "X-Trans",
            RawNative.SensorLinear => "Linear (already demosaiced)",
            RawNative.SensorMonochrome => "Monochrome",
            _ => "Other"
        });

        if (info.RawBits > 0)
            composite.AddFormatSpecific("Raw Bit Depth", $"{info.RawBits}-bit");

        var green = info.Multiplier(1);
        if (green > 0f && info.Multiplier(0) > 0f && info.Multiplier(2) > 0f)
            composite.AddFormatSpecific("As-Shot White Balance", $"R {info.Multiplier(0) / green:0.00}, B {info.Multiplier(2) / green:0.00}");

        if (info.DngVersion != 0)
        {
            var v = info.DngVersion;
            composite.AddFormatSpecific("DNG Version", $"{v >> 24}.{(v >> 16) & 0xFF}.{(v >> 8) & 0xFF}.{v & 0xFF}");
        }

        composite.AddFormatSpecific("Embedded Preview", info.PreviewFormat == RawNative.PreviewNone
            ? "None"
            : $"{info.PreviewWidth}x{info.PreviewHeight} {(info.PreviewFormat == RawNative.PreviewJpeg ? "JPEG" : "RGB")}");

        composite.AddFormatSpecific("Demosaic", info.Sensor is RawNative.SensorBayer or RawNative.SensorXTrans ? "AHD" : "None");
        composite.AddFormatSpecific("Decoder", $"LibRaw {RawNative.LibRawVersion}");
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var image = RawNative.OpenFile(path, out var info);
        if (image is null)
            return null;

        var bitmap = DecodePreview(image, info, maxDimension, ct) ?? DevelopSmall(image, info, ct);
        return bitmap is null ? null : ThumbnailScaler.ResizeToThumbnail(bitmap, maxDimension);
    }

    /// <summary>
    /// The camera's embedded preview, upright, decoded no larger than needed for
    /// <paramref name="maxDimension"/>; null when the file has none LibRaw can hand over.
    /// </summary>
    internal static SKBitmap? DecodePreview(RawNative.ImageHandle image, RawNative.RawInfo info, int maxDimension, CancellationToken ct) =>
        RawNative.raw_extract_preview(image, maxDimension, out var preview) ? DecodePreview(preview, info, maxDimension, ct) : null;

    /// <summary>The preview LibRaw lists at <paramref name="index"/>, at full size and upright.</summary>
    internal static SKBitmap? DecodePreviewAt(RawNative.ImageHandle image, RawNative.RawInfo info, int index, CancellationToken ct) =>
        RawNative.raw_extract_preview_at(image, index, out var preview) ? DecodePreview(preview, info, int.MaxValue, ct) : null;

    /// <summary>Decodes an extracted preview and releases it.</summary>
    private static SKBitmap? DecodePreview(RawNative.Preview preview, RawNative.RawInfo info, int maxDimension, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            return preview.Format == RawNative.PreviewJpeg
                ? DecodeJpegPreview(preview, info, maxDimension)
                : DecodeRgbPreview(preview, info);
        }
        finally
        {
            RawNative.free_raw_preview(preview.Owner);
        }
    }

    private static unsafe SKBitmap? DecodeJpegPreview(RawNative.Preview preview, RawNative.RawInfo info, int maxDimension)
    {
        if (preview.Size == 0 || preview.Size > int.MaxValue)
            return null;

        SKColorSpace? declared = null;

        using var data = SKData.CreateCopy(preview.Data, (int)preview.Size);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            return null;

        var desiredScale = Math.Min(1f, (float)maxDimension / Math.Max(codec.Info.Width, codec.Info.Height));
        var scaled = codec.GetScaledDimensions(desiredScale);

        DecoderValidation.RequireAvailableMemory(scaled.Width, scaled.Height);

        if (codec.Info.ColorSpace is null)
        {
            using var stream = new UnmanagedMemoryStream((byte*)preview.Data, (long)preview.Size);
            declared = RawPreviewColor.Of(stream, info.PreviewColorSpace);
        }

        var bitmap = new SKBitmap(new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Premul, codec.Info.ColorSpace ?? declared));
        bitmap.Erase(SKColors.Black);
        
        var target = declared is null ? bitmap.Info : bitmap.Info.WithColorSpace(null);
        var result = codec.GetPixels(target, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            return null;
        }

        var origin = codec.EncodedOrigin != SKEncodedOrigin.TopLeft ? codec.EncodedOrigin : OriginOf(info.Flip);
        return OrientationTransform.Apply(bitmap, origin);
    }

    private static unsafe SKBitmap? DecodeRgbPreview(RawNative.Preview preview, RawNative.RawInfo info)
    {
        var width = preview.Width;
        var height = preview.Height;
        if (width <= 0 || height <= 0 || (ulong)preview.Size < (ulong)width * (ulong)height * 3)
            return null;

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque, SKColorSpace.CreateSrgb()));

        var src = (byte*)preview.Data;
        var dst = (byte*)bitmap.GetPixels();
        var rowBytes = bitmap.RowBytes;

        for (var y = 0; y < height; y++)
        {
            var s = src + (long)y * width * 3;
            var d = dst + (long)y * rowBytes;

            for (var x = 0; x < width; x++, s += 3, d += 4)
            {
                d[0] = s[0];
                d[1] = s[1];
                d[2] = s[2];
                d[3] = 255;
            }
        }

        return OrientationTransform.Apply(bitmap, OriginOf(info.Flip));
    }

    /// <summary>A half-size development, tone-mapped, for files without a usable preview.</summary>
    private static SKBitmap? DevelopSmall(RawNative.ImageHandle image, RawNative.RawInfo info, CancellationToken ct)
    {
        if (!TryDevelop(image, new RawNative.DevelopOptions { HalfSize = 1 }, ct, out var pixels, out _))
            return null;

        try
        {
            if (pixels.Width <= 0 || pixels.Height <= 0)
                return null;

            var bitmap = new SKBitmap(new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb()));

            try
            {
                unsafe
                {
                    var stops = Math.Clamp(RawExposure.FallbackStops + info.BaselineExposure, RawExposure.MinStops, RawExposure.MaxStops);
                    var rows = new Rgb16Rows((ushort*)pixels.Data, pixels.Width, pixels.Height, MathF.Pow(2f, stops));
                    HdrToneMap.ToBitmap(rows, bitmap, HdrToneMap.MeasureWhitePoint(rows), RawExposure.RangeFor(stops), ct, out _);
                }
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }

            return bitmap;
        }
        finally
        {
            RawNative.free_raw_pixels(pixels.Owner);
        }
    }

    /// <summary>LibRaw's flip as the EXIF origin it stands for.</summary>
    internal static SKEncodedOrigin OriginOf(int flip) => flip switch
    {
        3 => SKEncodedOrigin.BottomRight,
        5 => SKEncodedOrigin.LeftBottom,
        6 => SKEncodedOrigin.RightTop,
        _ => SKEncodedOrigin.TopLeft
    };

    private static ExifOrientation OrientationOf(int flip) => (ExifOrientation)OriginOf(flip);
}
