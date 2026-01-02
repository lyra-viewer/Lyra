using Lyra.Imaging.Psd.Parser.PsdReader;
using Lyra.Imaging.Psd.Parser.SectionData;

namespace Lyra.Imaging.Psd.Parser.SectionReaders;

internal static class AdditionalLayerInformationReader
{
    // TODO PSD constants
    private static ReadOnlySpan<byte> PhotoshopBlockSignature => "8BIM"u8;
    private static ReadOnlySpan<byte> PhotoshopLargeBlockSignature => "8B64"u8;

    public static AdditionalLayerInformation[] ReadAll(PsdBigEndianReader reader, long sectionEnd, bool isPsb)
    {
        var blocks = new List<AdditionalLayerInformation>();

        // Minimum header size:
        // PSD: sig(4) + key(4) + len(4) = 12
        // PSB: sig(4) + key(4) + len(8) = 16
        var minHeaderSize = isPsb ? 16 : 12;

        while (reader.Position < sectionEnd)
        {
            var remaining = sectionEnd - reader.Position;

            // Not enough bytes for another valid block; treat as padding
            if (remaining < minHeaderSize)
            {
                reader.Skip(remaining);
                break;
            }

            var signature = ReadPhotoshopBlockSignature(reader);
            var key = reader.ReadUInt32();
            long payloadLength;
            if (!isPsb)
            {
                payloadLength = reader.ReadUInt32();
            }
            else
            {
                payloadLength = AdditionalLayerInformationKeys.UsesLongLengthFieldInPsb(key)
                    ? checked((long)reader.ReadUInt64())
                    : reader.ReadUInt32();
            }

            var payloadOffset = reader.Position;

            // Bounds check
            if (payloadOffset + payloadLength > sectionEnd)
            {
                var errorInfo = new AdditionalLayerInformation(signature, key, payloadOffset, payloadLength);
                throw new InvalidDataException($"AdditionalLayerInformation '{errorInfo.KeyFourCC}' exceeds LayerAndMask section bounds.");
            }

            // Skip payload
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

    private static string ReadPhotoshopBlockSignature(PsdBigEndianReader reader)
    {
        Span<byte> buffer = stackalloc byte[4];
        reader.ReadExactly(buffer);

        if (buffer.SequenceEqual(PhotoshopBlockSignature))
            return "8BIM";

        if (buffer.SequenceEqual(PhotoshopLargeBlockSignature))
            return "8B64";

        throw new InvalidDataException("Invalid Photoshop block signature.");
    }
}