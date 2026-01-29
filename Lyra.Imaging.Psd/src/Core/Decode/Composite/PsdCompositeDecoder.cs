using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Color;
using Lyra.Imaging.Psd.Core.Decode.Color.ColorCalibration;
using Lyra.Imaging.Psd.Core.Decode.Decompressors;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Primitives;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.Composite;

public static class PsdCompositeDecoder
{
    private static readonly Dictionary<CompressionType, Func<IPsdDecompressor>> DecompressorFactories = new()
    {
        { CompressionType.Raw, () => new PsdRawDecompressor() },
        { CompressionType.Rle, () => new PsdRleDecompressor() },
        { CompressionType.Zip, () => new PsdZipDecompressor() },
        { CompressionType.ZipPredict, () => new PsdZipPredictDecompressor() }
    };

    #region Entry Points

    /// <summary>
    /// Decodes the composite image at the document's native size into a single contiguous RGBA surface.
    /// </summary>
    /// <param name="maxSurfaceBytes">
    /// Optional hard limit for the output surface allocation (width * height * bytesPerPixel).
    /// If exceeded, decoding fails with <see cref="NotSupportedException"/>.
    /// </param>
    public static RgbaSurface DecodeComposite(PsdDocument psdDocument, Stream stream, SurfaceFormat outputFormat, long? maxSurfaceBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(psdDocument);
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
            throw new NotSupportedException("PSD composite decode requires a seekable stream.");

        return DecodeFullSurface(psdDocument, stream, outputFormat, maxSurfaceBytes, cancellationToken);
    }

    /// <summary>
    /// Decodes a scaled-down composite preview (nearest-neighbor in the decompressor).
    /// The preview never scales up.
    /// </summary>
    public static RgbaSurface DecodeCompositePreview(PsdDocument psdDocument, Stream stream, SurfaceFormat outputFormat, int maxWidth, int maxHeight, long? maxSurfaceBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(psdDocument);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);

        if (!stream.CanSeek)
            throw new NotSupportedException("PSD composite decode requires a seekable stream.");

        var header = psdDocument.FileHeader;
        var (outW, outH) = ComputePreviewSize(header.Width, header.Height, maxWidth, maxHeight);

        return DecodePreviewSurface(psdDocument, stream, outputFormat, maxSurfaceBytes, outW, outH, cancellationToken);
    }

    /// <summary>
    /// Creates a tiled composite container (geometry only). Does NOT decode any tiles.
    /// Call <see cref="DecodeTiles"/> to populate tiles progressively.
    /// </summary>
    public static TiledCompositeImage DecodeCompositeTiled(PsdDocument psdDocument, Stream stream, SurfaceFormat outputFormat, long maxBytesPerTile, int? tileEdgeHint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(psdDocument);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytesPerTile);
        if (tileEdgeHint is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileEdgeHint));

        if (!stream.CanSeek)
            throw new NotSupportedException("PSD composite decode requires a seekable stream.");

        var header = psdDocument.FileHeader;
        var (tileW, tileH) = ChooseTileSize(outputFormat, maxBytesPerTile, tileEdgeHint);

        return new TiledCompositeImage(header.Width, header.Height, outputFormat, tileW, tileH);
    }

    public static void DecodeTiles(PsdDocument psdDocument, Stream stream, TiledCompositeImage tiled, SurfaceFormat outputFormat, long? maxSurfaceBytes, Action<int, int>? onTileReady, CancellationToken cancellationToken)
    {
        DecodeTiles(psdDocument, stream, tiled, outputFormat, maxSurfaceBytes, bandOrder: null, onTileReady, cancellationToken);
    }

    /// <summary>
    /// Decodes the document's composite payload into the provided tiled container,
    /// optionally using a caller-provided band (tileY) decode order (RAW/RLE only).
    /// ZIP variants will ignore bandOrder and remain single-pass.
    /// </summary>
    public static void DecodeTiles(PsdDocument psdDocument, Stream stream, TiledCompositeImage tiled, SurfaceFormat outputFormat, long? maxSurfaceBytes, IReadOnlyList<int>? bandOrder, Action<int, int>? onTileReady, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(psdDocument);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(tiled);

        if (!stream.CanSeek)
            throw new NotSupportedException("PSD composite decode requires a seekable stream.");

        var compression = psdDocument.ImageData.CompressionType;

        // RAW/RLE can restart-from-payload efficiently thanks to cheap skipping.
        // ZIP variants must remain single-pass.
        if (compression is CompressionType.Raw or CompressionType.Rle)
        {
            DecodeTilesCenterOutBandsRawRle(psdDocument, stream, tiled, outputFormat, maxSurfaceBytes, bandOrder, onTileReady, cancellationToken);
            return;
        }

        // Ignore bandOrder for ZIP variants
        DecodeTilesAllAtOnce(psdDocument, stream, tiled, outputFormat, maxSurfaceBytes, onTileReady, cancellationToken);
    }

    #endregion

    #region Internal Decode Implementations

    private static RgbaSurface DecodeFullSurface(PsdDocument psdDocument, Stream stream, SurfaceFormat outputFormat, long? maxSurfaceBytes, CancellationToken cancellationToken)
    {
        var header = psdDocument.FileHeader;
        var imageResources = psdDocument.ImageResources;
        var imageData = psdDocument.ImageData;
        var metadata = psdDocument.PsdMetadata;

        metadata.CompressionType = imageData.CompressionType.ToString();

        var requiredBytes = ComputeRgba8Bytes(header.Width, header.Height);

        if (requiredBytes > int.MaxValue)
            throw new NotSupportedException($"Image too large for a single contiguous RGBA8 surface: {header.Width}x{header.Height} requires {requiredBytes:N0} bytes.");

        if (maxSurfaceBytes is { } cap && requiredBytes > cap)
            throw new NotSupportedException($"Decode would allocate {requiredBytes:N0} bytes which exceeds limit {cap:N0}.");

        var reader = new PsdBigEndianReader(stream);

        var iccProfile = TryReadIccProfile(imageResources, reader);

        // IMPORTANT: ensure start of decompression at the composite payload.
        stream.Position = imageData.PayloadOffset;

        if (!DecompressorFactories.TryGetValue(imageData.CompressionType, out var factory))
            throw new NotSupportedException($"Compression {imageData.CompressionType} not supported.");

        var decompressor = factory();

        var planesImage = PsdDecompressorBase.AllocatePlaneImage(header);
        var sink = new PlaneImageRowSink(planesImage);

        Action<int>? reportRowsCompleted = null;

        IPlaneRowConsumer consumer = reportRowsCompleted is null
            ? sink
            : new ProgressRowConsumer(sink, reportRowsCompleted);

        decompressor.DecompressPlanesRowRegion(reader, header, 0, header.Height, consumer, cancellationToken);

        var ctx = new ColorModeContext(
            ColorMode: header.ColorMode,
            OutputFormat: outputFormat,
            IndexedPaletteRgb: null,
            IccProfile: iccProfile,
            PreferColorManagement: true
        );

        var processor = ColorModeProcessorFactory.GetProcessor(header.ColorMode);
        var processedSurface = processor.Process(sink.Image, ctx, cancellationToken);

        metadata.EmbeddedIccProfileName = IccProfileNameExtractor.GetProfileNameOrNull(iccProfile);
        metadata.EffectiveIccProfileName = processor.IccProfileUsed;

        return processedSurface;
    }

    private static RgbaSurface DecodePreviewSurface(PsdDocument psdDocument, Stream stream, SurfaceFormat outputFormat, long? maxSurfaceBytes, int outWidth, int outHeight, CancellationToken cancellationToken)
    {
        var header = psdDocument.FileHeader;
        var imageResources = psdDocument.ImageResources;
        var imageData = psdDocument.ImageData;
        var metadata = psdDocument.PsdMetadata;

        metadata.CompressionType = imageData.CompressionType.ToString();

        var requiredBytes = ComputeRgba8Bytes(outWidth, outHeight);

        if (requiredBytes > int.MaxValue)
            throw new NotSupportedException($"Preview too large for a single contiguous RGBA8 surface: {outWidth}x{outHeight} requires {requiredBytes:N0} bytes.");

        if (maxSurfaceBytes is { } cap && requiredBytes > cap)
            throw new NotSupportedException($"Preview decode would allocate {requiredBytes:N0} bytes which exceeds limit {cap:N0}.");

        var reader = new PsdBigEndianReader(stream);

        var iccProfile = TryReadIccProfile(imageResources, reader);

        stream.Position = imageData.PayloadOffset;

        if (!DecompressorFactories.TryGetValue(imageData.CompressionType, out var factory))
            throw new NotSupportedException($"Compression {imageData.CompressionType} not supported.");

        var decompressor = factory();

        var planes = decompressor.DecompressPreview(reader, header, outWidth, outHeight, cancellationToken);

        var ctx = new ColorModeContext(
            ColorMode: header.ColorMode,
            OutputFormat: outputFormat,
            IndexedPaletteRgb: null,
            IccProfile: iccProfile,
            PreferColorManagement: true
        );

        var processor = ColorModeProcessorFactory.GetProcessor(header.ColorMode);
        var processedSurface = processor.Process(planes, ctx, cancellationToken);

        metadata.EmbeddedIccProfileName = IccProfileNameExtractor.GetProfileNameOrNull(iccProfile);
        metadata.EffectiveIccProfileName = processor.IccProfileUsed;

        return processedSurface;
    }

    private static void DecodeTilesAllAtOnce(PsdDocument psdDocument, Stream stream, TiledCompositeImage tiled, SurfaceFormat outputFormat, long? maxSurfaceBytes, Action<int, int>? onTileReady, CancellationToken cancellationToken)
    {
        var header = psdDocument.FileHeader;
        var imageResources = psdDocument.ImageResources;
        var imageData = psdDocument.ImageData;
        var metadata = psdDocument.PsdMetadata;

        metadata.CompressionType = imageData.CompressionType.ToString();

        if (maxSurfaceBytes is { } cap)
        {
            var totalBytes = ComputeRgba8Bytes(header.Width, header.Height);
            if (totalBytes > cap)
                throw new NotSupportedException($"Decode would produce {totalBytes:N0} bytes of RGBA8 (tiled) which exceeds limit {cap:N0}.");
        }

        var reader = new PsdBigEndianReader(stream);

        var iccProfile = TryReadIccProfile(imageResources, reader);
        metadata.EmbeddedIccProfileName = IccProfileNameExtractor.GetProfileNameOrNull(iccProfile);

        stream.Position = imageData.PayloadOffset;

        if (!DecompressorFactories.TryGetValue(imageData.CompressionType, out var factory))
            throw new NotSupportedException($"Compression {imageData.CompressionType} not supported.");

        var decompressor = factory();

        var ctx = new ColorModeContext(
            ColorMode: header.ColorMode,
            OutputFormat: outputFormat,
            IndexedPaletteRgb: null,
            IccProfile: iccProfile,
            PreferColorManagement: true
        );

        var processor = ColorModeProcessorFactory.GetProcessor(header.ColorMode);
        var roles = CompositePlaneRoles.Get(header.ColorMode, header.NumberOfChannels);

        TilePlaneImageRowSink? sink = null;

        void OnTileCompleted(int tileX, int tileY, PlaneImage tilePlanes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tileSurface = processor.Process(tilePlanes, ctx, cancellationToken);
            tiled.SetTile(tileX, tileY, tileSurface);

            sink!.ReleaseTilePlanes(tileX, tileY);
            onTileReady?.Invoke(tileX, tileY);
        }

        sink = new TilePlaneImageRowSink(
            imageWidth: header.Width,
            imageHeight: header.Height,
            tileWidth: tiled.TileWidth,
            tileHeight: tiled.TileHeight,
            bitsPerChannel: header.Depth,
            roles: roles,
            onTileCompleted: OnTileCompleted);

        decompressor.DecompressPlanesRowRegion(reader, header, 0, header.Height, sink, cancellationToken);

        metadata.EffectiveIccProfileName = processor.IccProfileUsed;
    }

    private static IEnumerable<int> GetCenterOutBandIndices(int bandCount)
    {
        if (bandCount <= 0)
            yield break;

        var mid = (bandCount - 1) / 2;

        yield return mid;

        for (var d = 1; d < bandCount; d++)
        {
            var up = mid - d;
            if (up >= 0)
                yield return up;

            var down = mid + d;
            if (down < bandCount)
                yield return down;
        }
    }

    private static void DecodeTilesCenterOutBandsRawRle(PsdDocument psdDocument, Stream stream, TiledCompositeImage tiled, SurfaceFormat outputFormat, long? maxSurfaceBytes, IReadOnlyList<int>? bandOrder, Action<int, int>? onTileReady, CancellationToken cancellationToken)
    {
        var header = psdDocument.FileHeader;
        var imageResources = psdDocument.ImageResources;
        var imageData = psdDocument.ImageData;
        var metadata = psdDocument.PsdMetadata;

        metadata.CompressionType = imageData.CompressionType.ToString();

        if (maxSurfaceBytes is { } cap)
        {
            var totalBytes = ComputeRgba8Bytes(header.Width, header.Height);
            if (totalBytes > cap)
                throw new NotSupportedException($"Decode would produce {totalBytes:N0} bytes of RGBA8 (tiled) which exceeds limit {cap:N0}.");
        }

        var iccReader = new PsdBigEndianReader(stream);
        var iccProfile = TryReadIccProfile(imageResources, iccReader);
        metadata.EmbeddedIccProfileName = IccProfileNameExtractor.GetProfileNameOrNull(iccProfile);

        if (!DecompressorFactories.TryGetValue(imageData.CompressionType, out var factory))
            throw new NotSupportedException($"Compression {imageData.CompressionType} not supported.");

        var decompressor = factory();

        var ctx = new ColorModeContext(
            ColorMode: header.ColorMode,
            OutputFormat: outputFormat,
            IndexedPaletteRgb: null,
            IccProfile: iccProfile,
            PreferColorManagement: true
        );

        var processor = ColorModeProcessorFactory.GetProcessor(header.ColorMode);
        var roles = CompositePlaneRoles.Get(header.ColorMode, header.NumberOfChannels);

        TilePlaneImageRowSink? sink = null;

        void OnTileCompleted(int tileX, int tileY, PlaneImage tilePlanes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tileSurface = processor.Process(tilePlanes, ctx, cancellationToken);
            tiled.SetTile(tileX, tileY, tileSurface);

            sink!.ReleaseTilePlanes(tileX, tileY);
            onTileReady?.Invoke(tileX, tileY);
        }

        sink = new TilePlaneImageRowSink(
            imageWidth: header.Width,
            imageHeight: header.Height,
            tileWidth: tiled.TileWidth,
            tileHeight: tiled.TileHeight,
            bitsPerChannel: header.Depth,
            roles: roles,
            onTileCompleted: OnTileCompleted);

        var bandHeight = tiled.TileHeight;
        var bandCount = (header.Height + bandHeight - 1) / bandHeight;

        IEnumerable<int> bands =
            bandOrder is { Count: > 0 }
                ? NormalizeBandOrder(bandOrder, bandCount)
                : GetCenterOutBandIndices(bandCount);

        foreach (var bandIndex in bands)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var yStart = bandIndex * bandHeight;
            var yEnd = Math.Min(header.Height, yStart + bandHeight);

            // Restart from payload for each band (efficient for RAW/RLE; DO NOT do this for ZIP).
            stream.Position = imageData.PayloadOffset;
            var reader = new PsdBigEndianReader(stream);

            // Reset ordering validation for this pass.
            sink.BeginPass(yStart, yEnd);

            decompressor.DecompressPlanesRowRegion(reader, header, yStart, yEnd, sink, cancellationToken);
        }

        metadata.EffectiveIccProfileName = processor.IccProfileUsed;
    }

    private static IEnumerable<int> NormalizeBandOrder(IReadOnlyList<int> bandOrder, int bandCount)
    {
        // Ensure 0..bandCount-1 unique, preserve first occurrence order.
        var seen = new bool[bandCount];
        for (var i = 0; i < bandOrder.Count; i++)
        {
            var b = bandOrder[i];
            if ((uint)b >= (uint)bandCount)
                continue;

            if (seen[b])
                continue;

            seen[b] = true;
            yield return b;
        }
    }

    #endregion

    #region Helpers

    private static (int w, int h) ComputePreviewSize(int srcW, int srcH, int maxW, int maxH)
    {
        var scale = Math.Min(1.0, Math.Min((double)maxW / srcW, (double)maxH / srcH));
        var w = Math.Max(1, (int)Math.Floor(srcW * scale));
        var h = Math.Max(1, (int)Math.Floor(srcH * scale));
        return (w, h);
    }

    private static long ComputeRgba8Bytes(int width, int height) => width * 4L * height;

    private static (int tileW, int tileH) ChooseTileSize(SurfaceFormat format, long maxBytesPerTile, int? tileEdgeHint)
    {
        var bpp = format.PixelFormat switch
        {
            PixelFormat.Rgba8888 => 4,
            PixelFormat.Bgra8888 => 4,
            _ => throw new NotSupportedException($"Tile sizing not supported for pixel format: {format.PixelFormat}")
        };

        const int minEdge = 256;
        const int maxEdge = 4096 * 2; // TODO calculate?
        const int align = 64;

        if (tileEdgeHint is { } hint)
        {
            var edge = Math.Clamp(hint, minEdge, maxEdge);
            edge = (edge / align) * align;
            edge = Math.Max(edge, minEdge);
            return (edge, edge);
        }

        var maxPixels = Math.Max(1, maxBytesPerTile / bpp);
        var edgeAuto = (int)Math.Floor(Math.Sqrt(maxPixels));
        edgeAuto = Math.Clamp(edgeAuto, minEdge, maxEdge);
        edgeAuto = (edgeAuto / align) * align;
        edgeAuto = Math.Max(edgeAuto, minEdge);

        return (edgeAuto, edgeAuto);
    }

    private static byte[]? TryReadIccProfile(ImageResources resources, PsdBigEndianReader reader)
    {
        if (!ImageResourcesHelper.TryGetResourceBlock(resources, PsdSignatures.IccProfileResourceId, out var header))
            return null;

        return ImageResourcesHelper.ReadResourceBytes(reader, header);
    }

    #endregion
}