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
    /// <exception cref="NotSupportedException">
    /// Thrown when the stream is not seekable, the required output allocation exceeds limits,
    /// or the composite compression type is unsupported.
    /// </exception>
    public static ICompositeImage DecodeComposite(PsdDocument psdDocument, Stream stream, SurfaceFormat outputFormat, long? maxSurfaceBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(psdDocument);
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
            throw new NotSupportedException("PSD composite decode requires a seekable stream.");

        var surface = DecodeFullSurface(psdDocument, stream, outputFormat, maxSurfaceBytes, cancellationToken);
        return new SurfaceCompositeImage(surface, outputFormat);
    }

    /// <summary>
    /// Decodes a scaled-down composite preview (nearest-neighbor in the decompressor).
    /// The preview never scales up: if the document is already within the bounds,
    /// it is decoded at its native size.
    /// Output size is computed from <paramref name="maxWidth"/>/<paramref name="maxHeight"/>
    /// while preserving aspect ratio.
    /// </summary>
    /// <param name="maxSurfaceBytes">
    /// Optional hard limit for the preview surface allocation (outWidth * outHeight * bytesPerPixel).
    /// </param>
    public static ICompositeImage DecodeCompositePreview(PsdDocument psdDocument, Stream stream, SurfaceFormat outputFormat, int maxWidth, int maxHeight, long? maxSurfaceBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(psdDocument);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);

        if (!stream.CanSeek)
            throw new NotSupportedException("PSD composite decode requires a seekable stream.");

        var header = psdDocument.FileHeader;
        var (outW, outH) = ComputePreviewSize(header.Width, header.Height, maxWidth, maxHeight);

        var surface = DecodePreviewSurface(psdDocument, stream, outputFormat, maxSurfaceBytes, outW, outH, cancellationToken);
        return new SurfaceCompositeImage(surface, outputFormat);
    }

    /// <summary>
    /// Create a tiled composite container with tile geometry precomputed.
    /// This does NOT decode tiles yet (future: DecodeCompositeTile/DecodeTiles).
    /// </summary>
    /// <param name="maxBytesPerTile">
    /// Target upper bound for a single tile's pixel storage (tileWidth * tileHeight * bytesPerPixel).
    /// Used to choose tile dimensions.
    /// </param>
    /// <param name="tileEdgeHint">
    /// Optional preferred tile edge length (pixels). If specified, it is clamped and aligned.
    /// </param>
    public static ICompositeImage DecodeCompositeTiled(PsdDocument psdDocument, SurfaceFormat outputFormat, long maxBytesPerTile, int? tileEdgeHint = null)
    {
        ArgumentNullException.ThrowIfNull(psdDocument);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytesPerTile);
        if (tileEdgeHint is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileEdgeHint));

        var header = psdDocument.FileHeader;
        var (tileW, tileH) = ChooseTileSize(outputFormat, maxBytesPerTile, tileEdgeHint);

        // TODO
        
        return new TiledCompositeImage(header.Width, header.Height, outputFormat, tileW, tileH);
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

        // IMPORTANT INVARIANT:
        // ImageData.PayloadOffset points to the first byte AFTER the 2-byte compression field.
        stream.Position = imageData.PayloadOffset;

        if (!DecompressorFactories.TryGetValue(imageData.CompressionType, out var factory))
            throw new NotSupportedException($"Compression {imageData.CompressionType} not supported.");

        var decompressor = factory();
        decompressor.ValidatePayload(header, imageData);

        var planes = decompressor.DecompressPlanes(reader, header, cancellationToken);

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

    // Preview decode that never allocates full planes/surface
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
        metadata.EmbeddedIccProfileName = IccProfileNameExtractor.GetProfileNameOrNull(iccProfile);

        stream.Position = imageData.PayloadOffset;

        if (!DecompressorFactories.TryGetValue(imageData.CompressionType, out var factory))
            throw new NotSupportedException($"Compression {imageData.CompressionType} not supported.");

        var decompressor = factory();
        decompressor.ValidatePayload(header, imageData);

        var planes = decompressor.DecompressPlanesScaled(reader, header, outWidth, outHeight, cancellationToken);

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
        const int maxEdge = 4096;
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