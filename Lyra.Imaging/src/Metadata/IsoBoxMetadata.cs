using System.Buffers.Binary;
using System.Text;

namespace Lyra.Imaging.Metadata;

/// <summary>
/// Pulls EXIF and XMP payloads out of the ISOBMFF-style containers used by JPEG XL and JPEG 2000.
///
/// MetadataExtractor has no reader for either format, so without this their metadata was simply
/// unavailable - a JXL exported from a phone showed nothing at all. Walking the top-level boxes
/// is enough: both formats keep metadata there rather than nested inside the codestream.
///
/// Files that are bare codestreams (JXL starting 0xFF0A, J2K starting 0xFF4F) carry no boxes and
/// no metadata, and are reported as empty rather than as an error.
/// </summary>
internal static class IsoBoxMetadata
{
    private static ReadOnlySpan<byte> JxlSignature => [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A];
    private static ReadOnlySpan<byte> Jp2Signature => [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    // "JpgTiffExif->JP2", the UUID JP2 writers agreed on for an embedded TIFF/EXIF block.
    private static ReadOnlySpan<byte> ExifUuid => [0x4A, 0x70, 0x67, 0x54, 0x69, 0x66, 0x66, 0x45, 0x78, 0x69, 0x66, 0x2D, 0x3E, 0x4A, 0x50, 0x32];

    // The UUID Adobe defined for an XMP packet in any ISOBMFF-style container.
    private static ReadOnlySpan<byte> XmpUuid => [0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8, 0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC];

    public readonly record struct Payloads(byte[]? Exif, byte[]? Xmp)
    {
        public bool IsEmpty => Exif is null && Xmp is null;
    }

    /// <summary>JPEG XL keeps EXIF in an "Exif" box and XMP in an "xml " box.</summary>
    public static Payloads ReadJxl(ReadOnlySpan<byte> data)
    {
        if (!data.StartsWith(JxlSignature))
            return default;

        byte[]? exif = null;
        byte[]? xmp = null;
        var offset = 0;

        while (TryReadBox(data, ref offset, out var type, out var payload))
        {
            if (type is "Exif" && exif is null)
                exif = ExtractTiff(payload);
            else if (type is "xml " && xmp is null && payload.Length > 0)
                xmp = payload.ToArray();
        }

        return new Payloads(exif, xmp);
    }

    /// <summary>JPEG 2000 keeps both in "uuid" boxes, told apart by their leading UUID.</summary>
    public static Payloads ReadJp2(ReadOnlySpan<byte> data)
    {
        if (!data.StartsWith(Jp2Signature))
            return default;

        byte[]? exif = null;
        byte[]? xmp = null;
        var offset = 0;

        while (TryReadBox(data, ref offset, out var type, out var payload))
        {
            if (type is not "uuid" || payload.Length <= 16)
                continue;

            var uuid = payload[..16];
            var content = payload[16..];

            if (uuid.SequenceEqual(ExifUuid) && exif is null)
                exif = ExtractTiff(content);
            else if (uuid.SequenceEqual(XmpUuid) && xmp is null)
                xmp = content.ToArray();
        }

        return new Payloads(exif, xmp);
    }

    /// <summary>
    /// Reads one box and advances past it. Returns false at the end of the data or on anything
    /// malformed - a truncated or lying size ends the walk rather than throwing, because this
    /// runs on files that may well be damaged.
    /// </summary>
    private static bool TryReadBox(ReadOnlySpan<byte> data, ref int offset, out string type, out ReadOnlySpan<byte> payload)
    {
        type = string.Empty;
        payload = default;

        // A box header is at minimum a 4-byte size and a 4-byte type.
        if (offset < 0 || data.Length - offset < 8)
            return false;

        var size = (long)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        type = Encoding.ASCII.GetString(data.Slice(offset + 4, 4));
        var headerSize = 8;

        switch (size)
        {
            case 1: // size 1 means a 64-bit length follows the type
                if (data.Length - offset < 16)
                    return false;

                size = (long)BinaryPrimitives.ReadUInt64BigEndian(data[(offset + 8)..]);
                headerSize = 16;
                break;

            case 0: // size 0 means the box runs to the end of the file
                size = data.Length - offset;
                break;
        }

        if (size < headerSize || size > data.Length - offset)
            return false;

        payload = data.Slice(offset + headerSize, (int)(size - headerSize));
        offset += (int)size;
        return true;
    }

    /// <summary>
    /// Finds the TIFF header in an EXIF payload.
    ///
    /// JXL prefixes it with a 4-byte offset, JP2 does not, and writers get this wrong in both
    /// directions - so the header is located rather than assumed, and a payload that never
    /// produces one is discarded instead of being handed on as garbage.
    /// </summary>
    private static byte[]? ExtractTiff(ReadOnlySpan<byte> payload)
    {
        if (StartsWithTiffHeader(payload))
            return payload.ToArray();

        if (payload.Length < 4)
            return null;

        var start = 4L + BinaryPrimitives.ReadUInt32BigEndian(payload);
        if (start >= payload.Length)
            return null;

        var candidate = payload[(int)start..];
        return StartsWithTiffHeader(candidate) ? candidate.ToArray() : null;
    }

    private static bool StartsWithTiffHeader(ReadOnlySpan<byte> data) =>
        data.Length >= 4
        && (
            (data[0] == 0x49 && data[1] == 0x49 && data[2] == 0x2A && data[3] == 0x00)    // "II*\0", little-endian
            || (data[0] == 0x4D && data[1] == 0x4D && data[2] == 0x00 && data[3] == 0x2A) // "MM\0*", big-endian
        );
}