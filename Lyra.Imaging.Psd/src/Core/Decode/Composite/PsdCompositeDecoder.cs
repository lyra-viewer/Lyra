using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.ColorCalibration;
using Lyra.Imaging.Psd.Core.Decode.ColorProcessors;
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

    public static RgbaSurface DecodeComposite(PsdDocument psdDocument, Stream stream, SurfaceFormat outputFormat, long? maxSurfaceBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(psdDocument);
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
            throw new NotSupportedException("PSD composite decode requires a seekable stream.");

        return DecodeFullSurface(psdDocument, stream, outputFormat, maxSurfaceBytes, ct);
    }

    public static RgbaSurface DecodeCompositePreview(PsdDocument psdDocument, Stream stream, SurfaceFormat outputFormat, int maxWidth, int maxHeight, long? maxSurfaceBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(psdDocument);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);

        if (!stream.CanSeek)
            throw new NotSupportedException("PSD composite decode requires a seekable stream.");

        var header = psdDocument.FileHeader;
        var (outW, outH) = ComputePreviewSize(header.Width, header.Height, maxWidth, maxHeight);

        return DecodePreviewSurface(psdDocument, stream, outputFormat, maxSurfaceBytes, outW, outH, ct);
    }

    public static TiledCompositeImage DecodeCompositeTiled(PsdDocument psdDocument, Stream stream, SurfaceFormat outputFormat, long maxBytesPerTile, int? tileEdgeHint)
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

    public static void DecodeTiles(PsdDocument psdDocument, Stream stream, TiledCompositeImage tiled, SurfaceFormat outputFormat, long? maxSurfaceBytes, Action<int, int>? onTileReady, CancellationToken ct)
    {
        DecodeTiles(psdDocument, stream, tiled, outputFormat, maxSurfaceBytes, bandOrder: null, onTileReady, ct);
    }

    public static void DecodeTiles(PsdDocument psdDocument, Stream stream, TiledCompositeImage tiled, SurfaceFormat outputFormat, long? maxSurfaceBytes, IReadOnlyList<int>? bandOrder, Action<int, int>? onTileReady, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(psdDocument);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(tiled);

        if (!stream.CanSeek)
            throw new NotSupportedException("PSD composite decode requires a seekable stream.");

        var compression = psdDocument.ImageData.CompressionType;

        // RAW/RLE can restart from payload cheaply (skipping).
        // ZIP variants must remain single-pass.
        if (compression is CompressionType.Raw or CompressionType.Rle)
        {
            DecodeTilesCenterOutBandsRawRle(psdDocument, stream, tiled, outputFormat, maxSurfaceBytes, bandOrder, onTileReady, ct);
            return;
        }

        DecodeTilesAllAtOnce(psdDocument, stream, tiled, outputFormat, maxSurfaceBytes, onTileReady, ct);
    }

    #endregion

    #region Internal Decode Implementations

    private static RgbaSurface DecodeFullSurface(PsdDocument psdDocument, Stream stream, SurfaceFormat outputFormat, long? maxSurfaceBytes, CancellationToken ct)
    {
        var header = psdDocument.FileHeader;
        var imageResources = psdDocument.ImageResources;
        var imageData = psdDocument.ImageData;
        var colorModeData = psdDocument.ColorModeData;
        var metadata = psdDocument.PsdMetadata;

        metadata.CompressionType = imageData.CompressionType.ToString();

        _ = PsdDepthUtil.FromBitsPerChannel(header.Depth);

        var requiredBytes = ComputeRgba8Bytes(header.Width, header.Height);
        if (requiredBytes > int.MaxValue)
            throw new NotSupportedException($"Image too large for a single contiguous RGBA8 surface: {header.Width}x{header.Height} requires {requiredBytes:N0} bytes.");

        if (maxSurfaceBytes is { } cap && requiredBytes > cap)
            throw new NotSupportedException($"Decode would allocate {requiredBytes:N0} bytes which exceeds limit {cap:N0}.");

        var reader = new PsdBigEndianReader(stream);

        var iccProfile = TryReadIccProfilePreservePosition(stream, imageResources, reader);

        stream.Position = imageData.PayloadOffset;

        var decompressor = CreateDecompressor(imageData.CompressionType);

        var allocated = PsdDecompressorBase.AllocatePlaneImage(header);
        var sink = new PlaneImageRowSink(allocated);

        decompressor.DecompressPlanesRowRegion(reader, header, 0, header.Height, sink, ct);

        var ctx = new ColorModeContext(
            ColorMode: header.ColorMode,
            OutputFormat: outputFormat,
            IndexedPaletteRgb: null,
            IccProfile: iccProfile,
            PreferColorManagement: true
        );

        var processor = ColorModeProcessorFactory.GetProcessor(header.ColorMode);
        var processed = processor.Process(sink.Image, ctx, colorModeData, ct);

        metadata.EmbeddedIccProfileName = IccProfileNameExtractor.GetProfileNameOrNull(iccProfile);
        metadata.EffectiveIccProfileName = processor.IccProfileUsed;

        return processed;
    }

    private static RgbaSurface DecodePreviewSurface(PsdDocument psdDocument, Stream stream, SurfaceFormat outputFormat, long? maxSurfaceBytes, int outWidth, int outHeight, CancellationToken ct)
    {
        var header = psdDocument.FileHeader;
        var imageResources = psdDocument.ImageResources;
        var imageData = psdDocument.ImageData;
        var colorModeData = psdDocument.ColorModeData;
        var metadata = psdDocument.PsdMetadata;

        metadata.CompressionType = imageData.CompressionType.ToString();

        _ = PsdDepthUtil.FromBitsPerChannel(header.Depth);

        var requiredBytes = ComputeRgba8Bytes(outWidth, outHeight);
        if (requiredBytes > int.MaxValue)
            throw new NotSupportedException($"Preview too large for a single contiguous RGBA8 surface: {outWidth}x{outHeight} requires {requiredBytes:N0} bytes.");

        if (maxSurfaceBytes is { } cap && requiredBytes > cap)
            throw new NotSupportedException($"Preview decode would allocate {requiredBytes:N0} bytes which exceeds limit {cap:N0}.");

        var reader = new PsdBigEndianReader(stream);

        var iccProfile = TryReadIccProfilePreservePosition(stream, imageResources, reader);

        stream.Position = imageData.PayloadOffset;

        var decompressor = CreateDecompressor(imageData.CompressionType);
        var planes = decompressor.DecompressPreview(reader, header, outWidth, outHeight, ct);

        var ctx = new ColorModeContext(
            ColorMode: header.ColorMode,
            OutputFormat: outputFormat,
            IndexedPaletteRgb: null,
            IccProfile: iccProfile,
            PreferColorManagement: true
        );

        var processor = ColorModeProcessorFactory.GetProcessor(header.ColorMode);
        var processed = processor.Process(planes, ctx, colorModeData, ct);

        metadata.EmbeddedIccProfileName = IccProfileNameExtractor.GetProfileNameOrNull(iccProfile);
        metadata.EffectiveIccProfileName = processor.IccProfileUsed;

        return processed;
    }

    private static void DecodeTilesAllAtOnce(PsdDocument psdDocument, Stream stream, TiledCompositeImage tiled, SurfaceFormat outputFormat, long? maxSurfaceBytes, Action<int, int>? onTileReady, CancellationToken ct)
    {
        var header = psdDocument.FileHeader;
        var imageResources = psdDocument.ImageResources;
        var imageData = psdDocument.ImageData;
        var colorModeData = psdDocument.ColorModeData;
        var metadata = psdDocument.PsdMetadata;

        metadata.CompressionType = imageData.CompressionType.ToString();

        _ = PsdDepthUtil.FromBitsPerChannel(header.Depth);

        if (maxSurfaceBytes is { } cap)
        {
            var total = ComputeRgba8Bytes(header.Width, header.Height);
            if (total > cap)
                throw new NotSupportedException($"Decode would produce {total:N0} bytes of RGBA8 (tiled) which exceeds limit {cap:N0}.");
        }

        var reader = new PsdBigEndianReader(stream);
        var iccProfile = TryReadIccProfilePreservePosition(stream, imageResources, reader);

        metadata.EmbeddedIccProfileName = IccProfileNameExtractor.GetProfileNameOrNull(iccProfile);

        stream.Position = imageData.PayloadOffset;

        var decompressor = CreateDecompressor(imageData.CompressionType);

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
            ct.ThrowIfCancellationRequested();

            var tileSurface = processor.Process(tilePlanes, ctx, colorModeData, ct);
            tiled.SetTile(tileX, tileY, tileSurface);

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

        decompressor.DecompressPlanesRowRegion(reader, header, 0, header.Height, sink, ct);

        metadata.EffectiveIccProfileName = processor.IccProfileUsed;
    }

    private static void DecodeTilesCenterOutBandsRawRle(PsdDocument psdDocument, Stream stream, TiledCompositeImage tiled, SurfaceFormat outputFormat, long? maxSurfaceBytes, IReadOnlyList<int>? bandOrder, Action<int, int>? onTileReady, CancellationToken ct)
    {
        var header = psdDocument.FileHeader;
        var imageResources = psdDocument.ImageResources;
        var imageData = psdDocument.ImageData;
        var colorModeData = psdDocument.ColorModeData;
        var metadata = psdDocument.PsdMetadata;

        metadata.CompressionType = imageData.CompressionType.ToString();

        _ = PsdDepthUtil.FromBitsPerChannel(header.Depth);

        if (maxSurfaceBytes is { } cap)
        {
            var total = ComputeRgba8Bytes(header.Width, header.Height);
            if (total > cap)
                throw new NotSupportedException($"Decode would produce {total:N0} bytes of RGBA8 (tiled) which exceeds limit {cap:N0}.");
        }

        var iccReader = new PsdBigEndianReader(stream);
        var iccProfile = TryReadIccProfilePreservePosition(stream, imageResources, iccReader);

        metadata.EmbeddedIccProfileName = IccProfileNameExtractor.GetProfileNameOrNull(iccProfile);

        var decompressor = CreateDecompressor(imageData.CompressionType);

        var ctx = new ColorModeContext(
            ColorMode: header.ColorMode,
            OutputFormat: outputFormat,
            IndexedPaletteRgb: null,
            IccProfile: iccProfile,
            PreferColorManagement: true
        );

        var processor = ColorModeProcessorFactory.GetProcessor(header.ColorMode);
        var roles = CompositePlaneRoles.Get(header.ColorMode, header.NumberOfChannels);

        var sink = new TilePlaneImageRowSink(
            imageWidth: header.Width,
            imageHeight: header.Height,
            tileWidth: tiled.TileWidth,
            tileHeight: tiled.TileHeight,
            bitsPerChannel: header.Depth,
            roles: roles,
            onTileCompleted: OnTileCompleted);

        var bandHeight = tiled.TileHeight;
        var bandCount = (header.Height + bandHeight - 1) / bandHeight;
        var bands = bandOrder is { Count: > 0 } ? NormalizeBandOrder(bandOrder, bandCount) : GetCenterOutBandIndices(bandCount);
        
        foreach (var bandIndex in bands)
        {
            ct.ThrowIfCancellationRequested();

            var yStart = bandIndex * bandHeight;
            var yEnd = Math.Min(header.Height, yStart + bandHeight);

            // Restart from payload for each band (efficient for RAW/RLE; DO NOT do this for ZIP).
            stream.Position = imageData.PayloadOffset;

            var reader = new PsdBigEndianReader(stream);

            decompressor.DecompressPlanesRowRegion(reader, header, yStart, yEnd, sink, ct);
        }

        metadata.EffectiveIccProfileName = processor.IccProfileUsed;
        return;

        void OnTileCompleted(int tileX, int tileY, PlaneImage tilePlanes)
        {
            ct.ThrowIfCancellationRequested();

            var tileSurface = processor.Process(tilePlanes, ctx, colorModeData, ct);
            tiled.SetTile(tileX, tileY, tileSurface);

            onTileReady?.Invoke(tileX, tileY);
        }
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

    private static IEnumerable<int> NormalizeBandOrder(IReadOnlyList<int> bandOrder, int bandCount)
    {
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
        const int maxEdge = 4096 * 2;
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

    private static IPsdDecompressor CreateDecompressor(CompressionType compression)
    {
        if (!DecompressorFactories.TryGetValue(compression, out var factory))
            throw new NotSupportedException($"Compression {compression} not supported.");

        return factory();
    }

    private static byte[]? TryReadIccProfilePreservePosition(Stream stream, ImageResources resources, PsdBigEndianReader reader)
    {
        var pos = stream.Position;
        try
        {
            return TryReadIccProfile(resources, reader);
        }
        finally
        {
            stream.Position = pos;
        }
    }

    private static byte[]? TryReadIccProfile(ImageResources resources, PsdBigEndianReader reader)
    {
        if (!ImageResourcesHelper.TryGetResourceBlock(resources, PsdSignatures.IccProfileResourceId, out var header))
            return null;

        return ImageResourcesHelper.ReadResourceBytes(reader, header);
    }

    #endregion
}