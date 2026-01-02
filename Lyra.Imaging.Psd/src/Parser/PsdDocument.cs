using Lyra.Imaging.Psd.Parser.PsdReader;
using Lyra.Imaging.Psd.Parser.SectionData;
using Lyra.Imaging.Psd.Parser.SectionReaders;

namespace Lyra.Imaging.Psd.Parser;

public sealed class PsdDocument
{
    public FileHeader FileHeader { get; }
    public ColorModeData ColorModeData { get; }
    public ImageResources ImageResources { get; }
    public LayerAndMaskInformation LayerAndMaskInformation { get; }
    public ImageData ImageData { get; }

    private PsdDocument(
        FileHeader fileHeader,
        ColorModeData colorModeData,
        ImageResources imageResources,
        LayerAndMaskInformation layerAndMaskInformation,
        ImageData imageData
    )
    {
        FileHeader = fileHeader;
        ColorModeData = colorModeData;
        ImageResources = imageResources;
        LayerAndMaskInformation = layerAndMaskInformation;
        ImageData = imageData;
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

        return new PsdDocument(header, colorMode, resources, layerAndMaskInformation, imageData);
    }
}