using System.Buffers.Binary;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Photoshop;
using Lyra.Metadata.Support;

namespace Lyra.Metadata.Containers;

public sealed record JpegFacts(byte FrameMarker, int Precision, int Components, int Width, int Height) : IFormatFacts
{
    /// <summary>SOF3: lossless Huffman, which is how raw data in a JPEG wrapper is coded.</summary>
    public bool Lossless => FrameMarker == 0xC3;
}

/// <summary>
/// Finds the metadata in a JPEG's header segments, before its image data: EXIF and XMP in APP1,
/// ICC in APP2, Photoshop's resources in APP13, and the frame header. Blocks come out in the order
/// a reader takes them - every APP1 EXIF, then every APP1 XMP, then the ICC profile, then what
/// APP13 holds - whatever order the segments are in, so the first of each kind is the one used.
/// </summary>
public sealed class JpegContainer : IBufferedContainer
{
    private const byte Marker = 0xFF;
    private const byte StartOfImage = 0xD8;
    private const byte StartOfScan = 0xDA;
    private const byte EndOfImage = 0xD9;
    private const byte App1 = 0xE1;
    private const byte App2 = 0xE2;
    private const byte App13 = 0xED;

    private const int MaxSegments = 4096;

    private static ReadOnlySpan<byte> XmpPreamble => "http://ns.adobe.com/xap/1.0/\0"u8;
    private static ReadOnlySpan<byte> IccPreamble => "ICC_PROFILE\0"u8;
    private static ReadOnlySpan<byte> PhotoshopPreamble => "Photoshop 3.0\0"u8;

    public static bool Recognizes(ReadOnlySpan<byte> head) => head is [Marker, StartOfImage, Marker, ..];

    public static ContainerContents? Read(ReadOnlySpan<byte> data)
    {
        if (!Recognizes(data))
            return null;

        var result = new ContainerContents { DamageWithNothingFails = true };
        var exif = new List<byte[]>();
        var xmp = new List<byte[]>();
        var icc = new List<byte>();
        var photoshop = new List<byte[]>();

        var offset = 2;
        for (var segments = 0; segments < MaxSegments; segments++)
        {
            if (offset + 2 > data.Length || data[offset] != Marker)
            {
                result.Damaged = offset < data.Length;
                break;
            }

            var marker = data[offset + 1];

            // Fill bytes before a marker.
            if (marker == Marker)
            {
                offset++;
                continue;
            }

            if (marker is StartOfScan or EndOfImage)
                break;

            // Markers that stand alone: RSTn and TEM.
            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
            {
                offset += 2;
                continue;
            }

            if (offset + 4 > data.Length)
            {
                result.Damaged = true;
                break;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
            if (length < 2 || offset + 2 + length > data.Length)
            {
                result.Damaged = true;
                break;
            }

            var payload = data.Slice(offset + 4, length - 2);

            if (marker == App1 && ExifPayload.HasPreamble(payload))
                exif.Add(payload[ExifPayload.Preamble.Length..].ToArray());
            else if (marker == App1 && payload.StartsWith(XmpPreamble))
                xmp.Add(payload[XmpPreamble.Length..].ToArray());
            else if (marker == App2 && payload.StartsWith(IccPreamble) && payload.Length > IccPreamble.Length + 2 && icc.Count + payload.Length <= Limits.AssembledBytes)
                icc.AddRange(payload[(IccPreamble.Length + 2)..]);
            else if (marker == App13 && payload.StartsWith(PhotoshopPreamble))
                photoshop.Add(payload[PhotoshopPreamble.Length..].ToArray());
            else if (IsFrameHeader(marker) && result.Fact<JpegFacts>() is null && payload.Length >= 6)
                result.AddFacts(new JpegFacts(marker, payload[0], payload[5],
                    BinaryPrimitives.ReadUInt16BigEndian(payload[3..]), BinaryPrimitives.ReadUInt16BigEndian(payload[1..])));

            offset += 2 + length;
        }

        foreach (var block in exif)
            result.Add(MetadataBlockKind.Exif, block);

        foreach (var block in xmp)
            result.Add(MetadataBlockKind.Xmp, block);

        if (icc.Count > 0)
            result.Add(MetadataBlockKind.Icc, [.. icc]);

        foreach (var block in photoshop)
            result.AddPhotoshop(PhotoshopResources.Parse(block).Resources);

        return result;
    }

    /// <summary>SOF0-SOF15, except DHT (C4), JPG (C8) and DAC (CC), which share the range.</summary>
    private static bool IsFrameHeader(byte marker) => marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC);
}