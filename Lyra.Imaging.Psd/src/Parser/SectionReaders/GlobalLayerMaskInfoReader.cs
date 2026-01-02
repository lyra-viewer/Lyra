using Lyra.Imaging.Psd.Parser.PsdReader;
using Lyra.Imaging.Psd.Parser.SectionData;

namespace Lyra.Imaging.Psd.Parser.SectionReaders;

internal static class GlobalLayerMaskInfoReader
{
    public static GlobalLayerMaskSummary Read(PsdBigEndianReader reader, long sectionEnd)
    {
        var payloadLength = (long)reader.ReadUInt32();
        if (reader.CanSeek && reader.Position + payloadLength > sectionEnd)
            throw new InvalidDataException("GlobalLayerMaskInfo exceeds bounds.");

        var payloadOffset = reader.Position;
        reader.Skip(payloadLength);

        return new GlobalLayerMaskSummary(payloadOffset, payloadLength);
    }
}