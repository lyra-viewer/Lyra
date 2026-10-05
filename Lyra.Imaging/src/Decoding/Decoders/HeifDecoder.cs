using System.Text;
using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders.Animation;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Metadata;
using Lyra.ManagedCodecs.Raster.Heif;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class HeifDecoder : DecoderBase, IThumbnailDecoder
{
    public override bool CanDecode(ImageFormatType format) => format == ImageFormatType.Heif;
    
    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        using var stream = new MeasuredReadStream(DecoderIO.OpenRandomAccessRead(path), composite.ReportTransferred, composite.CompleteTransfer);
        using var file = HeifFile.Open(stream, ct);
        using var still = file.HasPrimaryImage ? file.PrimaryImage() : null;

        Describe(composite, file, still, stream);

        if (TryPublishSequence(composite, file, still, stream, path, ct))
            return;

        if (still is null)
            throw new LoadFailureException(LoadFailureKind.DecodeFailed, "The file has no image to show");

        composite.ReportPixelCount(still.Width, still.Height);

        var bitmap = still.DecodeRgba(ResolveColorSpace(still), ct);

        // A sequence that could not be shown may have read the metadata already.
        composite.ExifInfo ??= ParseMetadata(still, stream, path);
        composite.AppliedOrientation = composite.ExifInfo.ContainerRotation;

        composite.Content = RasterContentBuilder.Build(bitmap, composite);
    }

    /// <summary>
    /// Publishes the file's image sequence frame by frame. False when it has none, or none that
    /// can be shown faithfully; the still image is then decoded instead.
    /// </summary>
    private static bool TryPublishSequence(Composite composite, HeifFile file, HeifImageHandle? still, Stream stream, string path, CancellationToken ct)
    {
        var movie = IsoTrackReader.ReadMovie(stream);

        // A lone sample is a sequence only when there is no still image to show instead.
        if (movie?.ColorTrack is not { SampleCount: > 0 } color || (color.SampleCount == 1 && still is not null))
            return false;

        if (!color.PlaysSamplesInOrder)
            return Unshown(still, path, "The file's image sequence has an edit list that reorders its frames, which is not supported");

        using var sequence = OpenSequence(file, movie, color, out var why);
        if (sequence is null)
            return Unshown(still, path, why);

        var size = sequence.Size;

        try
        {
            DecoderValidation.RequireSaneDimensions(size.Width, size.Height);
            DecoderValidation.RequireAvailableMemory(size.Width, size.Height);
        }
        catch (InvalidOperationException e) when (still is not null)
        {
            return Unshown(still, path, $"The file's image sequence cannot be shown: {e.Message}");
        }

        composite.ReportPixelCount(size.Width, size.Height);
        composite.ExifInfo = ParseMetadata(still, stream, path);

        var durations = color.SampleDurations.Select(ticks => TicksToMs(ticks, color.Timescale)).ToArray();
        var alpha = sequence.AlphaTrack;

        ct.ThrowIfCancellationRequested();

        // The frames share the still image's color space; a sequence-only file has none to share.
        var colorSpace = still is null ? null : ResolveColorSpace(still);
        var source = new HeifSequenceFrameSource(() => DecoderIO.OpenHeldRead(path), color.Id, alpha?.Id, color.SampleCount, size, colorSpace, composite);

        try
        {
            AnimatedContent.Publish(
                composite, path, source, damage: null,
                () => FrameFacts.WholeFrames(durations, size.Width, size.Height, EncodedBytes(color, alpha)),
                "HEIF",
                ct);
        }
        catch (LoadFailureException e) when (still is not null)
        {
            // The sequence's first frame is broken, but the file's still image may not be.
            Logger.Warning(typeof(HeifDecoder), $"{Path.GetFileName(path)}'s sequence could not be decoded ({e.Message}); showing its still image.");
            composite.AddFormatSpecific("Sequence", $"unreadable: {e.Message}");
            return false;
        }

        composite.AddFormatSpecific("Frames", color.SampleCount.ToString());
        composite.AddFormatSpecific("Duration", FrameFacts.TotalDuration(durations));

        if (sequence.Track.Repetitions is { } repetitions)
            composite.AddFormatSpecific("Loop", DescribeRepetitions(repetitions));

        return true;
    }

    private static bool Unshown(HeifImageHandle? still, string path, string reason)
    {
        if (still is null)
            throw new LoadFailureException(LoadFailureKind.DecodeFailed, reason);

        Logger.Warning(typeof(HeifDecoder), $"{Path.GetFileName(path)}: {reason}; showing its still image.");
        return false;
    }

    private sealed record Sequence(HeifTrack Track, SKSizeI Size, IsoTrack? AlphaTrack) : IDisposable
    {
        public void Dispose() => Track.Dispose();
    }
    
    private static Sequence? OpenSequence(HeifFile file, IsoMovie movie, IsoTrack color, out string reason)
    {
        reason = "";

        if (!HeifNative.SequencesAvailable)
        {
            reason = $"The file holds an image sequence, which needs libheif 1.20 or later (this is {HeifNative.Version})";
            return null;
        }

        var track = file.HasSequence ? file.Track(color.Id) : null;
        if (track?.Size is not { } size)
        {
            track?.Dispose();
            reason = "libheif cannot read the file's image sequence";
            return null;
        }

        return new Sequence(track, size, track.HasAlphaChannel ? null : movie.AlphaTrackFor(color));
    }

    private static string DescribeRepetitions(uint repetitions) =>
        repetitions == HeifNative.RepetitionsInfinite ? "forever" : FrameFacts.DescribePlays((int)Math.Min(repetitions, int.MaxValue));

    /// <summary>A frame's color sample and, when it has a track of its own, its alpha sample.</summary>
    private static Func<int, long?>? EncodedBytes(IsoTrack color, IsoTrack? alpha)
    {
        if (color.SampleSizes.Count != color.SampleCount)
            return null;

        var alphaSizes = alpha?.SampleSizes.Count == color.SampleCount ? alpha.SampleSizes : null;
        return i => color.SampleSizes[i] + (long)(alphaSizes?[i] ?? 0);
    }

    private static int TicksToMs(uint ticks, uint timescale) => timescale == 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Round(ticks * 1000.0 / timescale));

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var stream = DecoderIO.OpenRandomAccessRead(path);
        using var file = HeifFile.Open(stream, ct);

        var bitmap = file.HasPrimaryImage ? DecodeStillThumbnail(file, maxDimension, ct) : DecodeFirstFrame(file, stream, ct);
        if (bitmap is null)
            return null;

        ct.ThrowIfCancellationRequested();
        return ThumbnailScaler.ResizeToThumbnail(bitmap, maxDimension);
    }

    private static SKBitmap DecodeStillThumbnail(HeifFile file, int maxDimension, CancellationToken ct)
    {
        using var primary = file.PrimaryImage();
        using var embedded = TryGetEmbeddedThumbnail(primary, maxDimension);

        return (embedded ?? primary).DecodeRgba(colorSpace: null, ct);
    }

    private static SKBitmap? DecodeFirstFrame(HeifFile file, Stream stream, CancellationToken ct)
    {
        if (IsoTrackReader.ReadMovie(stream) is not { ColorTrack: { } color } movie)
            return null;

        using var sequence = OpenSequence(file, movie, color, out _);
        if (sequence is null)
            return null;

        DecoderValidation.RequireSaneDimensions(sequence.Size.Width, sequence.Size.Height);

        using var alpha = sequence.AlphaTrack is { } alphaTrack ? file.Track(alphaTrack.Id) : null;
        using var frame = HeifFrame.DecodeNext(sequence.Track, alpha);

        return frame?.ToBitmap(sequence.Size, colorSpace: null, ct);
    }

    private static ExifInfo ParseMetadata(HeifImageHandle? image, Stream stream, string path)
    {
        stream.Position = 0;
        var fromFile = MetadataProcessor.ParseMetadata(stream, path);

        if (fromFile.IsValid() && fromFile.HasData())
            return fromFile;

        if (image?.Exif() is not { } exif)
            return fromFile;

        using var block = new MemoryStream(exif, writable: false);
        return MetadataProcessor.ParseMetadata(block, path);
    }

    /// <summary>The first embedded thumbnail at least <paramref name="maxDimension"/> on its longest side.</summary>
    private static HeifImageHandle? TryGetEmbeddedThumbnail(HeifImageHandle image, int maxDimension)
    {
        try
        {
            foreach (var id in image.ThumbnailIds())
            {
                var thumbnail = image.Thumbnail(id);
                if (Math.Max(thumbnail.Width, thumbnail.Height) >= maxDimension)
                    return thumbnail;

                thumbnail.Dispose();
            }
        }
        catch (HeifException ex)
        {
            Logger.Debug(typeof(HeifDecoder), $"Embedded thumbnail lookup failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// The embedded color profile as an SKColorSpace: ICC when there is one, else the NCLX (CICP)
    /// tags common in camera HEIC and AVIF. Null when there is none, or one not mapped here (PQ
    /// and HLG among them), leaving sRGB assumed.
    /// </summary>
    private static SKColorSpace? ResolveColorSpace(HeifImageHandle image)
    {
        if (image.IccProfile() is { Length: > 0 } icc && SKColorSpace.CreateIcc(icc) is { } fromIcc)
            return fromIcc;

        return image.Nclx() is { } nclx ? FromNclx(nclx) : null;
    }

    /// <summary>Codes from ITU-T H.273. Only the display-relevant SDR gamuts and curves are mapped.</summary>
    private static SKColorSpace? FromNclx(HeifNative.NclxProfile nclx)
    {
        SKColorSpaceXyz? gamut = nclx.ColorPrimaries switch
        {
            1  => SKColorSpaceXyz.Srgb,        // BT.709
            12 => SKColorSpaceXyz.DisplayP3,   // SMPTE EG 432-1
            9  => SKColorSpaceXyz.Rec2020,     // BT.2020
            _  => null
        };

        // The gamma-type SDR curves are close enough to sRGB for display; linear is kept.
        SKColorSpaceTransferFn? curve = nclx.TransferCharacteristics switch
        {
            1 or 6 or 11 or 13 or 14 or 15 => SKColorSpaceTransferFn.Srgb,  // BT.709, BT.601, IEC 61966-2-4, sRGB, BT.2020 10/12-bit
            8 => SKColorSpaceTransferFn.Linear,
            _ => null
        };

        return gamut is { } g && curve is { } t ? SKColorSpace.CreateRgb(t, g) : null;
    }

    private static void Describe(Composite composite, HeifFile file, HeifImageHandle? image, Stream stream)
    {
        composite.AddFormatSpecific("Codec", DetectHeifBrand(stream));

        if (image is not null)
        {
            composite.AddFormatSpecific("Has Alpha", image.HasAlphaChannel);
            composite.AddFormatSpecific("Depth Map", image.HasDepthImage);

            if (image.ThumbnailIds() is { Length: > 0 } thumbnails)
                composite.AddFormatSpecific("Thumbnails", thumbnails.Length.ToString());
        }

        if (file.TopLevelImageCount is > 1 and var count)
            composite.AddFormatSpecific("Top-level Images", count.ToString());
    }

    private static string DetectHeifBrand(Stream stream)
    {
        // ftyp: box size, "ftyp", then the major brand.
        Span<byte> bytes = stackalloc byte[12];
        stream.Position = 0;

        if (stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) < bytes.Length)
            return "Unknown";

        var brand = Encoding.ASCII.GetString(bytes.Slice(8, 4));
        return brand switch
        {
            "heic" or "heix" or "heim" or "heis" or "hevc" or "hevx" => "HEVC",
            "avif" or "avis" => "AV1",
            "jpeg" or "jpgs" => "JPEG",
            "mif1" or "msf1" => "HEIF (generic)",
            _ => brand
        };
    }
}