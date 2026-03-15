using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;
using Lyra.Imaging.Psd.Core.SectionReaders;

namespace Lyra.Imaging.Psd.Core.Decode.Layers;

public static class PsdLayerDecoder
{
    /// <summary>
    /// Decode layer records from the LayerInfo payload.
    /// Resolves each layer's name via 'luni' (Unicode) with Pascal string fallback.
    /// </summary>
    public static LayerRecord[] DecodeLayerRecords(PsdDocument psdDocument, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(psdDocument);
        ArgumentNullException.ThrowIfNull(stream);

        var header = psdDocument.FileHeader;
        var layerInfo = psdDocument.LayerAndMaskInformation.LayerInfo;

        if (layerInfo.PayloadLength == 0 || layerInfo.LayerCount == 0)
            return [];

        if (!stream.CanSeek)
            throw new NotSupportedException("Layer decoding requires a seekable stream.");

        var isPsb = header.Version == 2;
        var reader = new PsdBigEndianReader(stream)
        {
            Position = layerInfo.PayloadOffset
        };

        var count = Math.Abs(reader.ReadInt16());
        if (count == 0)
            return [];

        var channelEntrySize = 2 + (isPsb ? 8 : 4);
        var records = new LayerRecord[count];

        for (var i = 0; i < count; i++)
        {
            var top = reader.ReadInt32();
            var left = reader.ReadInt32();
            var bottom = reader.ReadInt32();
            var right = reader.ReadInt32();

            var channelCount = reader.ReadUInt16();
            reader.Skip(channelCount * channelEntrySize);

            // Blend mode signature + key, opacity, clipping, flags, filler
            reader.Skip(4 + 4 + 4);

            var extraDataLength = reader.ReadUInt32();
            var extraDataEnd = reader.Position + extraDataLength;

            SkipLayerMaskData(reader);
            SkipLayerBlendingRanges(reader);

            var pascalName = reader.ReadPascalString(padTo: 4);
            var unicodeName = TryReadUnicodeLayerName(reader, extraDataEnd, isPsb);

            if (reader.Position != extraDataEnd)
                reader.Position = extraDataEnd;

            records[i] = new LayerRecord(top, left, bottom, right, unicodeName ?? pascalName);
        }

        return records;
    }

    private static string? TryReadUnicodeLayerName(PsdBigEndianReader reader, long extraDataEnd, bool isPsb)
    {
        if (reader.Position >= extraDataEnd)
            return null;

        var perLayerInfo = AdditionalLayerInformationReader.ReadAll(reader, extraDataEnd, isPsb);
        foreach (var block in perLayerInfo)
        {
            if (block.Key != AdditionalLayerInformationKeys.UnicodeLayerName)
                continue;

            reader.Position = block.PayloadOffset;
            return reader.ReadUnicodeString();
        }

        return null;
    }

    private static void SkipLayerMaskData(PsdBigEndianReader reader)
    {
        var length = reader.ReadUInt32();
        if (length > 0)
            reader.Skip(length);
    }

    private static void SkipLayerBlendingRanges(PsdBigEndianReader reader)
    {
        var length = reader.ReadUInt32();
        if (length > 0)
            reader.Skip(length);
    }
}