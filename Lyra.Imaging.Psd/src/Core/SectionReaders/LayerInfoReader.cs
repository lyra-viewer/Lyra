using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.SectionReaders;

internal static class LayerInfoReader
{
    public static LayerInfo Read(PsdBigEndianReader reader, long sectionEnd, bool isPsb, FileHeader header)
    {
        // LayerInfo length is 4 bytes for PSD, 8 bytes for PSB
        var payloadLength = isPsb ? checked((long)reader.ReadUInt64()) : reader.ReadUInt32();
        var payloadOffset = reader.Position;
        var payloadEnd = checked(payloadOffset + payloadLength);

        // Bound checks
        if (payloadEnd > sectionEnd)
            throw new InvalidDataException("LayerInfo exceeds LayerAndMask section bounds.");

        if (payloadLength == 0)
            return new LayerInfo(payloadOffset, 0, 0);

        // Layer count is the first field inside the payload (signed 16-bit)
        // Only read if there are at least 2 bytes
        short layerCount = 0;
        if (payloadLength >= 2)
        {
            layerCount = reader.ReadInt16();
            // Consumed 2 bytes, so remaining payload is payloadLength - 2
            reader.Skip(payloadLength - 2);
        }
        else
        {
            // payloadLength == 1 is corrupt; skip to preserve alignment
            reader.Skip(payloadLength);
        }

        // Ensure position lands exactly at payloadEnd
        if (reader.CanSeek && reader.Position != payloadEnd)
            reader.Skip(payloadEnd - reader.Position);

        return new LayerInfo(payloadOffset, payloadLength, layerCount);
    }
}