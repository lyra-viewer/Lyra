using Lyra.Imaging.Psd.Adapters.SkiaSharp;
using Lyra.Imaging.Psd.Core.Decode.Composite;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;
using Lyra.Imaging.Psd.Core.SectionReaders;
using SkiaSharp;

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
        PsdMetadata psdMetadata
    )
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
        if (stream.CanSeek)
            stream.Position = 0;

        var reader = new PsdBigEndianReader(stream);
        return FileHeaderSectionReader.Read(reader);
    }

    public static PsdDocument ReadDocument(Stream stream)
    {
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

    public static SKImage Decode(Stream stream, CancellationToken ct) => Decode(stream, SurfaceFormat.Default, ct);

    public static SKImage Decode(Stream stream, SurfaceFormat outputFormat, CancellationToken ct)
    {
        var psd = ReadDocument(stream);
        var composite = PsdCompositeDecoder.DecodeComposite(psd, stream, outputFormat, null, ct);
        var result = SkiaCompositeConverter.Convert(composite);
        
        return result switch
        {
            SkiaCompositeResult.Single s => s.Image,
            SkiaCompositeResult.Tiled => throw new NotSupportedException("Decode() must return a single image. Use DecodeTiled() for tiled results."),
            _ => throw new NotSupportedException($"Unknown SkiaCompositeResult type: {result.GetType().FullName}")
        };
    }

    public static SKImage DecodePreview(Stream stream, int maxWidth, int maxHeight, long? maxSurfaceBytes, CancellationToken ct)
    {
        return DecodePreview(stream, SurfaceFormat.Default, maxWidth, maxHeight, maxSurfaceBytes, ct);
    }

    public static SKImage DecodePreview(Stream stream, SurfaceFormat outputFormat, int maxWidth, int maxHeight, long? maxSurfaceBytes, CancellationToken ct)
    {
        var psd = ReadDocument(stream);
        var composite = PsdCompositeDecoder.DecodeCompositePreview(psd, stream, outputFormat, maxWidth, maxHeight, maxSurfaceBytes, ct);
        var result = SkiaCompositeConverter.Convert(composite);
        
        return result switch
        {
            SkiaCompositeResult.Single s => s.Image,
            SkiaCompositeResult.Tiled => throw new NotSupportedException("Decode() must return a single image. Use DecodeTiled() for tiled results."),
            _ => throw new NotSupportedException($"Unknown SkiaCompositeResult type: {result.GetType().FullName}")
        };
    }

    public static SkiaCompositeResult DecodeTiled(Stream stream, SurfaceFormat outputFormat, long maxBytesPerTile, int? tileEdgeHint, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}