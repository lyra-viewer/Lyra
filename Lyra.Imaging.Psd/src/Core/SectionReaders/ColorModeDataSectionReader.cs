using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.SectionReaders;

// Only indexed color and duotone have color mode data.
// For now, only the lenght is stored and whole section is skipped.
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

        // Skip payload (0 for most modes, 768 for Indexed, variable for Duotone)
        reader.Skip(length);

        return new ColorModeData(length);
    }
}