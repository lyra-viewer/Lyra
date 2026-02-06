using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.SectionReaders;

// Only indexed color and duotone have color mode data.
// Indexed: 768 bytes palette (R[256], G[256], B[256])
// Duotone: variable length (Photoshop-specific)
internal static class ColorModeDataSectionReader
{
    public static ColorModeData Read(PsdBigEndianReader reader)
    {
        var length = reader.ReadUInt32();

        if (reader.CanSeek)
        {
            var remaining = reader.Length - reader.Position;
            if (length > remaining)
                throw new InvalidDataException($"ColorModeDataSection length {length} exceeds remaining stream bytes {remaining}.");
        }

        if (length == 0)
            return new ColorModeData(0, null);

        if (length > int.MaxValue)
            throw new NotSupportedException($"ColorModeDataSection length {length} is too large for a single byte array.");

        var data = new byte[(int)length];
        reader.ReadExactly(data);
        return new ColorModeData(length, data);
    }
}