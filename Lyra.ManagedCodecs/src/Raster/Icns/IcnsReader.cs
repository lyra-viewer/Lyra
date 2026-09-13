using System.Text;

namespace Lyra.ManagedCodecs.Raster.Icns;

public sealed record IcnsEntry(IcnsIconType Type, IcnsPayloadKind Kind, int PayloadOffset, int PayloadLength, int MaskOffset = 0, int MaskLength = 0)
{
    public int Width => Type.Width;
    public int Height => Type.Height;
    public int Scale => Type.Scale;

    public ReadOnlySpan<byte> Payload(byte[] data) => data.AsSpan(PayloadOffset, PayloadLength);
}

public static class IcnsReader
{
    private const int HeaderSize = 8;
    private const int ChunkHeaderSize = 8;
    
    public const int MinimumLength = HeaderSize + ChunkHeaderSize;

    public static bool IsIcns(ReadOnlySpan<byte> data) =>
        data.Length >= MinimumLength &&
        data[0] == (byte)'i' && data[1] == (byte)'c' && data[2] == (byte)'n' && data[3] == (byte)'s';
    
    public static IReadOnlyList<IcnsEntry> ReadEntries(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!IsIcns(data))
            return [];

        var declared = ReadUInt32BigEndian(data, 4);
        var end = declared is > HeaderSize and <= int.MaxValue
            ? Math.Min((int)declared, data.Length)
            : data.Length;

        var entries = new List<IcnsEntry>();
        var masks = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);

        var offset = HeaderSize;
        while (offset + ChunkHeaderSize <= end)
        {
            var code = Encoding.ASCII.GetString(data, offset, 4);
            var length = ReadUInt32BigEndian(data, offset + 4);

            // A chunk that does not span its own header, or claims to run past the container,
            // means the walk has lost sync - there is no way to find the next boundary.
            if (length < ChunkHeaderSize || offset + (long)length > end)
                break;

            var payloadOffset = offset + ChunkHeaderSize;
            var payloadLength = (int)length - ChunkHeaderSize;

            if (IcnsIconType.TryGet(code, out var type) && payloadLength > 0)
            {
                if (type.Kind == IcnsPayloadKind.Mask)
                    masks[code] = (payloadOffset, payloadLength);
                else
                    entries.Add(new IcnsEntry(type, DetectKind(data, payloadOffset, payloadLength, type), payloadOffset, payloadLength));
            }

            offset += (int)length;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (IcnsIconType.MaskCodeFor(entries[i].Type.Code) is { } maskCode
                && masks.TryGetValue(maskCode, out var mask))
                entries[i] = entries[i] with { MaskOffset = mask.Offset, MaskLength = mask.Length };
        }

        return entries
            .OrderByDescending(e => e.Width * e.Height)
            .ThenByDescending(e => e.Scale)
            .ThenBy(e => e.Type.Code, StringComparer.Ordinal)
            .ToList();
    }
    
    public static DecodedImage? Decode(byte[] data, IcnsEntry entry)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(entry);
        
        return entry.Kind switch
        {
            IcnsPayloadKind.Argb => DecodeArgb(data, entry) ?? DecodeRle24(data, entry),
            IcnsPayloadKind.Rle24 => DecodeRle24(data, entry) ?? DecodeArgb(data, entry),
            _ => null
        };
    }

    // ------------------------------------------------------------------
    //  Payload kind
    // ------------------------------------------------------------------
    
    private static IcnsPayloadKind DetectKind(byte[] data, int offset, int length, IcnsIconType type)
    {
        var payload = data.AsSpan(offset, length);

        if (StartsWith(payload, [0x89, (byte)'P', (byte)'N', (byte)'G'])           // PNG
            || StartsWith(payload, [0xFF, 0xD8, 0xFF])                             // JPEG
            || StartsWith(payload, [0x00, 0x00, 0x00, 0x0C, (byte)'j', (byte)'P']) // JP2
            || StartsWith(payload, [0xFF, 0x4F, 0xFF, 0x51]))                      // raw J2K codestream
            return IcnsPayloadKind.Embedded;

        if (StartsWith(payload, "ARGB"u8))
            return IcnsPayloadKind.Argb;
        
        return type.Kind == IcnsPayloadKind.Argb ? IcnsPayloadKind.Argb : IcnsPayloadKind.Rle24;
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
        data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);

    // ------------------------------------------------------------------
    //  ARGB entries
    // ------------------------------------------------------------------

    private static DecodedImage? DecodeArgb(byte[] data, IcnsEntry entry)
    {
        ReadOnlySpan<byte> payload = data.AsSpan(entry.PayloadOffset, entry.PayloadLength);

        if (StartsWith(payload, "ARGB"u8))
            payload = payload[4..];

        var count = entry.Width * entry.Height;
        var planes = ReadPlanes(payload, count, 4);
        if (planes is null)
            return null;

        var pixels = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            pixels[(i * 4) + 0] = planes[1][i];
            pixels[(i * 4) + 1] = planes[2][i];
            pixels[(i * 4) + 2] = planes[3][i];
            pixels[(i * 4) + 3] = planes[0][i];
        }

        return new DecodedImage(pixels, entry.Width, entry.Height);
    }

    // ------------------------------------------------------------------
    //  RLE24 entries + their mask
    // ------------------------------------------------------------------

    private static DecodedImage? DecodeRle24(byte[] data, IcnsEntry entry)
    {
        ReadOnlySpan<byte> payload = data.AsSpan(entry.PayloadOffset, entry.PayloadLength);

        if (entry.Type.Code == "it32" && payload.Length >= 4)
            payload = payload[4..];

        var count = entry.Width * entry.Height;
        var planes = ReadPlanes(payload, count, 3);
        if (planes is null)
            return null;

        var alpha = ReadMask(data, entry, count);

        var pixels = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            pixels[(i * 4) + 0] = planes[0][i];
            pixels[(i * 4) + 1] = planes[1][i];
            pixels[(i * 4) + 2] = planes[2][i];
            pixels[(i * 4) + 3] = alpha is null ? (byte)0xFF : alpha[i];
        }

        return new DecodedImage(pixels, entry.Width, entry.Height);
    }

    private static byte[]? ReadMask(byte[] data, IcnsEntry entry, int count)
    {
        if (entry.MaskLength < count)
            return null;

        return [.. data.AsSpan(entry.MaskOffset, count)];
    }

    // ------------------------------------------------------------------
    //  Plane decompression
    // ------------------------------------------------------------------
    
    private static byte[][]? ReadPlanes(ReadOnlySpan<byte> source, int planeLength, int planeCount)
    {
        var planes = new byte[planeCount][];
        var flat = source.Length == planeLength * planeCount;

        for (var i = 0; i < planeCount; i++)
        {
            planes[i] = new byte[planeLength];

            if (flat)
            {
                source[..planeLength].CopyTo(planes[i]);
                source = source[planeLength..];
                continue;
            }

            if (!ReadCompressedPlane(ref source, planes[i]))
                return null;
        }

        return planes;
    }
    
    private static bool ReadCompressedPlane(ref ReadOnlySpan<byte> source, byte[] plane)
    {
        var written = 0;
        var read = 0;

        while (written < plane.Length && read < source.Length)
        {
            var control = source[read++];
            if (control >= 0x80)
            {
                var runLength = control - 0x80 + 3;
                if (read >= source.Length)
                    return false;

                var value = source[read++];
                runLength = Math.Min(runLength, plane.Length - written);

                plane.AsSpan(written, runLength).Fill(value);
                written += runLength;
            }
            else
            {
                var literalLength = control + 1;
                literalLength = Math.Min(literalLength, Math.Min(plane.Length - written, source.Length - read));
                if (literalLength <= 0)
                    return false;

                source.Slice(read, literalLength).CopyTo(plane.AsSpan(written, literalLength));
                read += literalLength;
                written += literalLength;
            }
        }

        source = source[Math.Min(read, source.Length)..];
        return written == plane.Length;
    }

    private static uint ReadUInt32BigEndian(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
}