using Lyra.Imaging.Psd.Parser.Primitives;
using Lyra.Imaging.Psd.Parser.PsdReader;
using Lyra.Imaging.Psd.Parser.SectionData;

namespace Lyra.Imaging.Psd.Parser.SectionReaders;

internal static class AdditionalLayerInformationReader
{
    public static AdditionalLayerInformation[] ReadAll(PsdBigEndianReader reader, long sectionEnd, bool isPsb)
    {
        var blocks = new List<AdditionalLayerInformation>();

        while (reader.Position < sectionEnd)
        {
            var remaining = sectionEnd - reader.Position;

            // Need at least signature(4) + key(4)
            if (remaining < 8)
            {
                reader.Skip(remaining);
                break;
            }

            if (!reader.TryPeekSignature(PsdSignatures.PhotoshopBlock) &&
                !reader.TryPeekSignature(PsdSignatures.PhotoshopLargeBlock))
            {
                break;
            }

            var signature = ReadPhotoshopBlockSignature(reader);
            var key = reader.ReadUInt32();

            var lenFieldSize = (!isPsb || !AdditionalLayerInformationKeys.UsesLongLengthFieldInPsb(key)) ? 4 : 8;

            remaining = sectionEnd - reader.Position;
            if (remaining < lenFieldSize)
                throw new InvalidDataException($"Truncated AdditionalLayerInformation length field for '{FourCC.ToString(key)}'.");

            var payloadLength = lenFieldSize == 4
                ? reader.ReadUInt32()
                : checked((long)reader.ReadUInt64());

            var payloadOffset = reader.Position;

            if (payloadOffset + payloadLength > sectionEnd)
            {
                var errorInfo = new AdditionalLayerInformation(signature, key, payloadOffset, payloadLength);
                throw new InvalidDataException($"AdditionalLayerInformation '{errorInfo.KeyFourCC}' exceeds section bounds.");
            }

            reader.Skip(payloadLength);

            // Pad to even
            if ((payloadLength & 1) == 1)
            {
                if (reader.Position >= sectionEnd)
                    throw new InvalidDataException("AdditionalLayerInformation padding exceeds section bounds.");

                reader.Skip(1);
            }

            blocks.Add(new AdditionalLayerInformation(signature, key, payloadOffset, payloadLength));
        }

        return blocks.ToArray();
    }

    private static uint ReadPhotoshopBlockSignature(PsdBigEndianReader reader)
    {
        var pos = reader.Position;
        var sig = reader.ReadUInt32();

        if (PsdSignatures.IsPhotoshopBlock(sig))
            return sig;

        throw new InvalidDataException($"Invalid Photoshop block signature '{FourCC.ToString(sig)}' (0x{sig:X8}) at 0x{pos:X}.");
    }
}