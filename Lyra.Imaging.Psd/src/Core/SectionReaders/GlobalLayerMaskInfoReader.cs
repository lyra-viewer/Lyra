using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.SectionReaders;

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