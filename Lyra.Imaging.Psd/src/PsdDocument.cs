using Lyra.Imaging.Psd.Core.Decode.Composite;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;
using Lyra.Imaging.Psd.Core.SectionReaders;

namespace Lyra.Imaging.Psd;

public sealed class PsdDocument
{
    public FileHeader FileHeader { get; }
    public ColorModeData ColorModeData { get; }
    public ImageResources ImageResources { get; }
    public LayerAndMaskInformation LayerAndMaskInformation { get; }
    public ImageData ImageData { get; }

    public PsdMetadata PsdMetadata { get; }

    private PsdDocument(
        FileHeader fileHeader,
        ColorModeData colorModeData,
        ImageResources imageResources,
        LayerAndMaskInformation layerAndMaskInformation,
        ImageData imageData,
        PsdMetadata psdMetadata)
    {
        FileHeader = fileHeader;
        ColorModeData = colorModeData;
        ImageResources = imageResources;
        LayerAndMaskInformation = layerAndMaskInformation;
        ImageData = imageData;
        PsdMetadata = psdMetadata;
    }

    public static FileHeader ReadHeader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.CanSeek)
            stream.Position = 0;

        var reader = new PsdBigEndianReader(stream);
        return FileHeaderSectionReader.Read(reader);
    }

    public static PsdDocument ReadDocument(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.CanSeek)
            stream.Position = 0;

        var reader = new PsdBigEndianReader(stream);

        var header = FileHeaderSectionReader.Read(reader);
        var colorMode = ColorModeDataSectionReader.Read(reader);
        var resources = ImageResourcesSectionReader.Read(reader);
        var layerAndMaskInformation = LayerAndMaskInformationSectionReader.Read(reader, header);
        var imageData = ImageDataReader.Read(reader);

        return new PsdDocument(header, colorMode, resources, layerAndMaskInformation, imageData, new PsdMetadata());
    }

    /// <summary>
    /// Decode full-resolution composite into a contiguous RGBA surface.
    /// </summary>
    public RgbaSurface Decode(Stream stream, SurfaceFormat? outputFormat = null, long? maxSurfaceBytes = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.CanSeek)
            stream.Position = ImageData.PayloadOffset;

        return PsdCompositeDecoder.DecodeComposite(this, stream, outputFormat ?? SurfaceFormat.Default, maxSurfaceBytes, ct);
    }

    /// <summary>
    /// Decode a scaled-down composite preview (never scales up).
    /// </summary>
    public RgbaSurface DecodePreview(Stream stream, int maxWidth, int maxHeight, SurfaceFormat? outputFormat = null, long? maxSurfaceBytes = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.CanSeek)
            stream.Position = ImageData.PayloadOffset;

        return PsdCompositeDecoder.DecodeCompositePreview(this, stream, outputFormat ?? SurfaceFormat.Default, maxWidth, maxHeight, maxSurfaceBytes, ct);
    }

    /// <summary>
    /// Create a tiled composite container (geometry only). Does NOT decode tiles.
    /// </summary>
    public TiledCompositeImage CreateTiledComposite(Stream stream, long maxBytesPerTile, int? tileEdgeHint = null, SurfaceFormat? outputFormat = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.CanSeek)
            stream.Position = ImageData.PayloadOffset;

        return PsdCompositeDecoder.DecodeCompositeTiled(this, stream, outputFormat ?? SurfaceFormat.Default, maxBytesPerTile, tileEdgeHint, ct);
    }

    /// <summary>
    /// Decode composite payload into an existing tiled container (progressive-friendly).
    /// </summary>
    public void DecodeTiles(Stream stream, TiledCompositeImage tiled, SurfaceFormat? outputFormat = null, long? maxSurfaceBytes = null, Action<int, int>? onTileReady = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(tiled);

        if (stream.CanSeek)
            stream.Position = ImageData.PayloadOffset;

        PsdCompositeDecoder.DecodeTiles(this, stream, tiled, outputFormat ?? SurfaceFormat.Default, maxSurfaceBytes, onTileReady, ct);
    }

    /// <summary>
    /// Decode composite payload into an existing tiled container (progressive-friendly),
    /// using an optional caller-provided band (tileY) decode order.
    /// 
    /// Notes:
    /// - Band order is honored for RAW/RLE (restartable payload).
    /// - ZIP variants remain single-pass and will ignore bandOrder.
    /// </summary>
    public void DecodeTiles(Stream stream, TiledCompositeImage tiled, IReadOnlyList<int>? bandOrder, SurfaceFormat? outputFormat = null, long? maxSurfaceBytes = null, Action<int, int>? onTileReady = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(tiled);

        if (stream.CanSeek)
            stream.Position = ImageData.PayloadOffset;

        PsdCompositeDecoder.DecodeTiles(this, stream, tiled, outputFormat ?? SurfaceFormat.Default, maxSurfaceBytes, bandOrder, onTileReady, ct);
    }
}