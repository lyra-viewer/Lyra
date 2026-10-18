using Lyra.Metadata.Support;

namespace Lyra.Metadata.MakerNotes;

public sealed record MakerNoteLayout(long DirectoryOffset, bool LittleEndian, long? BaseOffset, MakerNoteVendor? Vendor);

/// <summary>
/// The camera's private directory under EXIF tag 0x927C. Most are a plain IFD; some put a header in
/// front and count offsets from the note itself, or carry a whole TIFF of their own.
/// </summary>
public static class MakerNote
{
    public static MakerNoteLayout Locate(ReadOnlySpan<byte> head, string? make, bool littleEndian)
    {
        var vendor = Vendor(head, make);

        if (head.StartsWith("Nikon\0"u8) && head.Length >= 18)
        {
            var little = head[10] == 'I';
            var first = ByteReader.UInt32(head[14..], little);
            return new MakerNoteLayout(10 + first, little, 10, vendor);
        }

        if (head.StartsWith("OLYMPUS\0"u8) && head.Length >= 12)
            return new MakerNoteLayout(12, head[8] == 'I', 0, vendor);

        if (head.StartsWith("OM SYSTEM\0"u8) && head.Length >= 16)
            return new MakerNoteLayout(16, head[12] == 'I', 0, vendor);

        if (head.StartsWith("Apple iOS\0"u8))
            return new MakerNoteLayout(14, false, 0, vendor);

        if (head.StartsWith("FUJIFILM"u8) && head.Length >= 12)
            return new MakerNoteLayout(ByteReader.UInt32(head[8..], little: true), true, 0, vendor);

        if (head.StartsWith("PENTAX \0"u8) && head.Length >= 10)
            return new MakerNoteLayout(10, head[8] == 'I', 0, vendor);

        if (head.StartsWith("AOC\0"u8) && head.Length >= 6)
            return new MakerNoteLayout(6, head[4] == 'I' || (head[4] == ' ' && littleEndian), null, vendor);

        if (head.StartsWith("Panasonic\0"u8) || head.StartsWith("SONY DSC \0"u8) || head.StartsWith("SONY CAM \0"u8))
            return new MakerNoteLayout(12, littleEndian, null, vendor);

        if (head.StartsWith("OLYMP\0"u8) || head.StartsWith("LEICA\0"u8) || head.StartsWith("SIGMA\0"u8))
            return new MakerNoteLayout(8, littleEndian, null, vendor);

        return new MakerNoteLayout(0, littleEndian, null, vendor);
    }

    private static MakerNoteVendor? Vendor(ReadOnlySpan<byte> head, string? make)
    {
        if (head.StartsWith("AOC\0"u8) || head.StartsWith("PENTAX \0"u8))
            return MakerNoteVendor.Pentax;

        var maker = make?.ToUpperInvariant() ?? string.Empty;
        return maker switch
        {
            _ when maker.StartsWith("CANON")     => MakerNoteVendor.Canon,
            _ when maker.StartsWith("NIKON")     => MakerNoteVendor.Nikon,
            _ when maker.StartsWith("SONY")      => MakerNoteVendor.Sony,
            _ when maker.StartsWith("PANASONIC") => MakerNoteVendor.Panasonic,
            _ when maker.StartsWith("FUJI")      => MakerNoteVendor.Fujifilm,
            _ when maker.StartsWith("APPLE")     => MakerNoteVendor.Apple,
            _ when maker.StartsWith("SAMSUNG")   => MakerNoteVendor.Samsung,
            _ when maker.StartsWith("LEICA")     => MakerNoteVendor.Leica,
            _ when maker.StartsWith("SIGMA")     => MakerNoteVendor.Sigma,
            _ when maker.StartsWith("PENTAX") || maker.StartsWith("RICOH") => MakerNoteVendor.Pentax,
            _ when maker.StartsWith("OLYMPUS") || maker.StartsWith("OM DIGITAL") => MakerNoteVendor.Olympus,
            _ => null
        };
    }
    
    public static bool LooksLikeDirectory(Stream stream, long offset, bool littleEndian)
    {
        Span<byte> countBytes = stackalloc byte[2];
        if (!stream.ReadAt(offset, countBytes))
            return false;

        var count = ByteReader.UInt16(countBytes, littleEndian);
        if (count is 0 or > 1000)
            return false;

        var entries = new byte[Math.Min(count, (ushort)16) * 12];
        if (!stream.ReadAt(offset + 2, entries))
            return false;

        var known = 0;
        for (var i = 0; i < entries.Length / 12; i++)
        {
            var type = ByteReader.UInt16(entries.AsSpan(i * 12 + 2), littleEndian);
            if (type is >= 1 and <= 13)
                known++;
        }

        return known * 5 >= entries.Length / 12 * 4;
    }
}