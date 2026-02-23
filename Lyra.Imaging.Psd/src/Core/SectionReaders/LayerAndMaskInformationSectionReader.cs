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
            // Align to 2 bytes if possible (defensive tolerance)
            if (((reader.Position - start) & 1) != 0 && reader.Position + 1 <= end)
                reader.Skip(1);

            if (GlobalLayerMaskInfoReader.TryRead(reader, end, out var gm))
                globalMask = gm;

            additional = AdditionalLayerInformationReader.ReadAll(reader, end, isPsb);
        }

        // Jump to end if padding remains (safe)
        if (reader.Position < end)
            reader.Skip(end - reader.Position);
        else if (reader.Position > end)
        {
            if (reader.CanSeek)
                reader.Position = end;
            else
                throw new InvalidDataException($"LayerAndMask parsing overran section end by {reader.Position - end} bytes.");
        }

        return new LayerAndMaskInformation(length, layerInfoSummary, globalMask, additional);
    }
}