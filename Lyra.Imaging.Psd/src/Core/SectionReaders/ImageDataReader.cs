using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.SectionReaders;

internal static class ImageDataReader
{
    public static ImageData Read(PsdBigEndianReader reader)
    {
        var compression = (CompressionType)reader.ReadUInt16();

        var payloadOffset = reader.Position;
        var payloadLength = reader.Length - payloadOffset;

        return new ImageData(payloadOffset, payloadLength, compression);
    }
}