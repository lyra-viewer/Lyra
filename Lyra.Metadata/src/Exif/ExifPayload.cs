using System.Buffers.Binary;

namespace Lyra.Metadata.Exif;

/// <summary>The ways containers wrap the TIFF that holds EXIF.</summary>
public static class ExifPayload
{
    public static ReadOnlySpan<byte> Preamble => "Exif\0\0"u8;

    public static bool HasPreamble(ReadOnlySpan<byte> payload) => payload.StartsWith(Preamble);
    
    public static ReadOnlySpan<byte> WithoutPreamble(ReadOnlySpan<byte> payload) => HasPreamble(payload) ? payload[Preamble.Length..] : payload;
    
    public static byte[]? LocateTiff(ReadOnlySpan<byte> payload)
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
    
    public static byte[]? FromHeifItem(byte[] item)
    {
        if (item.Length >= 4 && (item.AsSpan().StartsWith("MM"u8) || item.AsSpan().StartsWith("II*\0"u8)))
            return item;

        if (item.Length < 4)
            return null;

        var start = 4L + BinaryPrimitives.ReadUInt32BigEndian(item);
        return start <= item.Length ? item[(int)start..] : null;
    }

    private static bool StartsWithTiffHeader(ReadOnlySpan<byte> data) => data.StartsWith("II*\0"u8) || data.StartsWith("MM\0*"u8);
}