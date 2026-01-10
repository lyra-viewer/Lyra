using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.SectionReaders;

internal static class LayerAndMaskInformationSectionReader
{
    public static LayerAndMaskInformation Read(PsdBigEndianReader reader, FileHeader header)
    {
        var isPsb = header.Version == 2;

        var length = isPsb ? checked((long)reader.ReadUInt64()) : reader.ReadUInt32();
        var start = reader.Position;
        var end = checked(start + length);

        if (reader.CanSeek)
        {
            var remaining = reader.Length - start;
            if (length > remaining)
                throw new InvalidDataException($"LayerAndMask section length {length} exceeds remaining {remaining}.");
        }

        if (length == 0)
            return LayerAndMaskInformation.Empty;

        var layerInfoSummary = LayerInfoReader.Read(reader, end, isPsb, header);
        GlobalLayerMaskSummary globalMask = default;
        AdditionalLayerInformation[] additional = [];

        if (reader.Position < end)
        {
            globalMask = GlobalLayerMaskInfoReader.Read(reader, end);
            additional = AdditionalLayerInformationReader.ReadAll(reader, end, isPsb);
        }

        // Jump to end if small padding remains
        if (reader.Position != end)
            reader.Skip(end - reader.Position);

        return new LayerAndMaskInformation(length, layerInfoSummary, globalMask, additional);
    }
}