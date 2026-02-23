using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.SectionReaders;

internal static class GlobalLayerMaskInfoReader
{
    public static bool TryRead(PsdBigEndianReader reader, long sectionEnd, out GlobalLayerMaskSummary summary)
    {
        summary = default;

        if (reader.CanSeek && reader.Position + 4 > sectionEnd)
            return false;

        var lengthPos = reader.Position;

        var payloadLength = (long)reader.ReadUInt32();
        var payloadOffset = reader.Position;

        if (reader.CanSeek && payloadOffset + payloadLength > sectionEnd)
        {
            reader.Position = lengthPos;
            return false;
        }

        reader.Skip(payloadLength);
        summary = new GlobalLayerMaskSummary(payloadOffset, payloadLength);
        return true;
    }
}