using Lyra.Imaging.Psd.Parser.PsdReader;
using Lyra.Imaging.Psd.Parser.SectionData;

namespace Lyra.Imaging.Psd.Parser.SectionReaders;

internal static class ImageDataReader
{
    public static ImageData Read(PsdBigEndianReader reader)
    {
        var sectionStart = reader.Position;
        var compression = (CompressionType)reader.ReadUInt16();

        var payloadOffset = reader.Position;
        var payloadLength = reader.Length - payloadOffset;

        return new ImageData(payloadOffset, payloadLength, compression);
    }
}