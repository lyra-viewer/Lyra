using Lyra.Metadata.Support;
using static Lyra.Metadata.Support.ByteReader;

namespace Lyra.Metadata.Tiff;

/// <summary>One directory entry: where its value lives, not the value itself.</summary>
/// <param name="ValueOffset">Absolute offset of the value bytes in the file.</param>
public readonly record struct TiffEntry(ushort Tag, TiffFieldType Type, long Count, long ValueOffset, long ValueSize);

public readonly record struct TiffRational(long Numerator, long Denominator)
{
    public bool IsDefined => Denominator != 0;
}

public sealed class TiffDirectory
{
    public required long Offset { get; init; }
    public TiffDirectoryKind Kind { get; init; } = TiffDirectoryKind.Image;
    public required IReadOnlyList<TiffEntry> Entries { get; init; }
    public IReadOnlyList<TiffDirectory> Children { get; init; } = [];

    public IEnumerable<TiffDirectory> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var below in child.Descendants())
                yield return below;
        }
    }

    public TiffEntry? Find(ushort tag)
    {
        foreach (var entry in Entries)
            if (entry.Tag == tag)
                return entry;

        return null;
    }
}

/// <summary>The directory tree of a TIFF or BigTIFF, as laid out in the file.</summary>
public sealed class TiffLayout
{
    public required bool LittleEndian { get; init; }
    public bool BigTiff { get; init; }

    /// <summary>The header's version word: 42 for TIFF, 43 for BigTIFF, or a raw format's own.</summary>
    public ushort Version { get; init; }
    public required IReadOnlyList<TiffDirectory> Directories { get; init; }

    public IEnumerable<TiffDirectory> AllDirectories()
    {
        foreach (var directory in Directories)
        {
            yield return directory;
            foreach (var below in directory.Descendants())
                yield return below;
        }
    }
}

/// <summary>
/// Walks the directories of a TIFF or BigTIFF (camera raws and EXIF blocks among them) from a
/// seekable stream, trusting nothing in the file: every offset is checked against the stream,
/// chains and trees are bounded, and a loop ends the walk instead of repeating it.
/// </summary>
public static class TiffDirectoryReader
{
    public const ushort TagSubIfds = 330;
    public const ushort TagExifIfd = 0x8769;
    public const ushort TagGpsIfd = 0x8825;
    public const ushort TagInteropIfd = 0xA005;

    public const ushort ClassicVersion = 42;
    public const ushort BigTiffVersion = 43;
    public const ushort OlympusRawVersion = 0x4F52;    // "RO"
    public const ushort OlympusRawVersionAlt = 0x5352; // "SR"
    public const ushort PanasonicRawVersion = 0x55;

    private const int MaxDirectories = 64;
    private const int MaxEntries = 4096;
    private const int MaxDepth = 4;
    private const int MaxSubIfdsPerDirectory = 16;

    public static bool IsTiff(ReadOnlySpan<byte> header) =>
        header.Length >= 4 && TryByteOrder(header, out var little)
        && UInt16(header[2..], little) is ClassicVersion or BigTiffVersion or OlympusRawVersion or OlympusRawVersionAlt or PanasonicRawVersion;
    
    public static bool IsTiff(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 8)
            return false;

        using var _ = stream.KeepPosition();
        Span<byte> header = stackalloc byte[4];
        return stream.ReadAt(0, header) && IsTiff(header);
    }
    
    public static bool IsBigTiff(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 8)
            return false;

        using var _ = stream.KeepPosition();
        Span<byte> header = stackalloc byte[4];
        return stream.ReadAt(0, header) && TryByteOrder(header, out var little) && UInt16(header[2..], little) == BigTiffVersion;
    }

    public static bool IsBigTiff(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return IsBigTiff(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The directory tree, or null when the stream is neither a TIFF nor a BigTIFF.</summary>
    /// <param name="firstKind">The kind of the first directory, which decides the pointers followed from it: IFD0 but in CR3's EXIF and GPS blocks.</param>
    public static TiffLayout? Read(Stream stream, TiffDirectoryKind firstKind = TiffDirectoryKind.Image)
    {
        if (!stream.CanSeek || stream.Length < 8)
            return null;

        Span<byte> header = stackalloc byte[16];
        stream.Position = 0;
        var read = stream.ReadAtLeast(header, 16, throwOnEndOfStream: false);
        if (read < 8 || !TryByteOrder(header, out var little))
            return null;

        long first;
        bool big;
        var version = UInt16(header[2..], little);

        // 42 is TIFF; ORF ("RO", "SR") and RW2 (0x55) use their own magic over the same layout.
        switch (version)
        {
            case ClassicVersion or OlympusRawVersion or OlympusRawVersionAlt or PanasonicRawVersion:
                big = false;
                first = UInt32(header[4..], little);
                break;

            case BigTiffVersion when read == 16 && UInt16(header[4..], little) == 8 && UInt16(header[6..], little) == 0:
                big = true;
                first = ClampToLong(UInt64(header[8..], little));
                break;

            default:
                return null;
        }

        var cursor = new Cursor(stream, little, big);
        var visited = new HashSet<long>();
        var directories = new List<TiffDirectory>();

        var next = first;
        while (next != 0 && directories.Count < MaxDirectories)
        {
            var kind = directories.Count == 0 ? firstKind : TiffDirectoryKind.Image;
            if (ReadDirectory(cursor, next, kind, visited, depth: 0, out var following) is not { } directory)
                break;

            directories.Add(directory);
            next = following;
        }

        return directories.Count == 0 ? null : new TiffLayout { LittleEndian = little, BigTiff = big, Version = version, Directories = directories };
    }

    /// <summary>
    /// One classic directory at <paramref name="offset"/>, without following its pointers. Offsets
    /// stored in it count from <paramref name="baseOffset"/>, which is not 0 in the maker notes of
    /// cameras that treat the note as a file of its own. Null when there is no sane directory there.
    /// </summary>
    public static TiffDirectory? ReadDirectoryAt(Stream stream, long offset, bool littleEndian, out long next, long baseOffset = 0)
    {
        var cursor = new Cursor(stream, littleEndian, big: false);
        return ReadDirectory(cursor, offset, TiffDirectoryKind.Image, [], MaxDepth, out next, baseOffset);
    }

    public static long[]? ReadIntegers(Stream stream, TiffEntry entry, bool littleEndian, int maxCount)
    {
        var size = entry.Type switch
        {
            TiffFieldType.Byte or TiffFieldType.SByte => 1,
            TiffFieldType.Short or TiffFieldType.SShort => 2,
            TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Ifd => 4,
            TiffFieldType.Long8 or TiffFieldType.SLong8 or TiffFieldType.Ifd8 => 8,
            _ => 0
        };
        if (size == 0 || entry.Count > maxCount || ReadBytes(stream, entry, maxCount * size) is not { } bytes)
            return null;

        var values = new long[entry.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var b = bytes.AsSpan(i * size, size);
            values[i] = entry.Type switch
            {
                TiffFieldType.Byte => b[0],
                TiffFieldType.SByte => (sbyte)b[0],
                TiffFieldType.Short => UInt16(b, littleEndian),
                TiffFieldType.SShort => (short)UInt16(b, littleEndian),
                TiffFieldType.SLong => (int)UInt32(b, littleEndian),
                TiffFieldType.Long8 or TiffFieldType.Ifd8 => ClampToLong(UInt64(b, littleEndian)),
                TiffFieldType.SLong8 => (long)UInt64(b, littleEndian),
                _ => UInt32(b, littleEndian)
            };
        }

        return values;
    }

    /// <summary>RATIONAL or SRATIONAL values, as written; a zero denominator is left for the reader to judge.</summary>
    public static TiffRational[]? ReadRationals(Stream stream, TiffEntry entry, bool littleEndian, int maxCount)
    {
        if (entry.Type is not (TiffFieldType.Rational or TiffFieldType.SRational) || entry.Count > maxCount || ReadBytes(stream, entry, maxCount * 8) is not { } bytes)
            return null;

        var signed = entry.Type == TiffFieldType.SRational;
        var values = new TiffRational[entry.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var numerator = UInt32(bytes.AsSpan(i * 8), littleEndian);
            var denominator = UInt32(bytes.AsSpan(i * 8 + 4), littleEndian);
            values[i] = signed
                ? new TiffRational((int)numerator, (int)denominator)
                : new TiffRational(numerator, denominator);
        }

        return values;
    }

    public static float[]? ReadFloats(Stream stream, TiffEntry entry, bool littleEndian, int maxCount)
    {
        if (entry.Type != TiffFieldType.Float || entry.Count > maxCount || ReadBytes(stream, entry, maxCount * 4) is not { } bytes)
            return null;

        var values = new float[entry.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = BitConverter.Int32BitsToSingle((int)UInt32(bytes.AsSpan(i * 4), littleEndian));

        return values;
    }

    /// <summary>
    /// An ASCII value as text, ending at its first NUL and trimmed. Writers put UTF-8 there, which
    /// decodes the same for plain ASCII; bytes that are not UTF-8 fall back to Latin-1.
    /// </summary>
    public static string? ReadText(Stream stream, TiffEntry entry, int maxBytes)
    {
        if (entry.Type is not (TiffFieldType.Ascii or TiffFieldType.Undefined or TiffFieldType.Byte) || ReadBytes(stream, entry, maxBytes) is not { } bytes)
            return null;

        return ByteText.Utf8OrLatin1(ByteText.UntilNul(bytes)).Trim();
    }

    public static byte[]? ReadBytes(Stream stream, TiffEntry entry, int maxBytes) => entry.ValueSize > 0 ? stream.ReadBytesAt(entry.ValueOffset, entry.ValueSize, maxBytes) : null;

    private static TiffDirectory? ReadDirectory(Cursor cursor, long offset, TiffDirectoryKind kind, HashSet<long> visited, int depth, out long next, long baseOffset = 0)
    {
        next = 0;
        if (visited.Contains(offset) || ReadEntries(cursor, offset, baseOffset, out next) is not { } entries)
            return null;

        // Marked before the children are read, so none of them can lead back here.
        visited.Add(offset);
        var children = depth < MaxDepth ? ReadChildren(cursor, entries, kind, visited, depth, baseOffset) : [];

        return new TiffDirectory { Offset = offset, Kind = kind, Entries = entries, Children = children };
    }

    private static List<TiffEntry>? ReadEntries(Cursor cursor, long offset, long baseOffset, out long next)
    {
        next = 0;

        var countSize = cursor.Big ? 8 : 2;
        var entrySize = cursor.Big ? 20 : 12;
        var fieldSize = cursor.Big ? 8 : 4;

        if (offset < 8 || offset + countSize > cursor.Length)
            return null;

        Span<byte> countBytes = stackalloc byte[8];
        if (!cursor.ReadAt(offset, countBytes[..countSize]))
            return null;

        var declared = cursor.Big ? UInt64(countBytes, cursor.Little) : UInt16(countBytes, cursor.Little);
        if (declared is 0 or > MaxEntries)
            return null;

        // The entries must all be there; the pointer to the next directory, after them, need not be.
        var count = (int)declared;
        if (offset + countSize + (long)count * entrySize > cursor.Length)
            return null;

        var hasNext = offset + countSize + (long)count * entrySize + fieldSize <= cursor.Length;
        var raw = new byte[count * entrySize + (hasNext ? fieldSize : 0)];
        if (!cursor.ReadAt(offset + countSize, raw))
            return null;

        var entries = new List<TiffEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var e = raw.AsSpan(i * entrySize, entrySize);
            var tag = UInt16(e, cursor.Little);
            var type = (TiffFieldType)UInt16(e[2..], cursor.Little);
            var declaredCount = cursor.Big ? UInt64(e[4..], cursor.Little) : UInt32(e[4..], cursor.Little);
            var field = e.Slice(cursor.Big ? 12 : 8, fieldSize);

            var unit = TypeSize(type);
            if (unit == 0 || declaredCount == 0 || declaredCount > (ulong)(long.MaxValue / unit))
                continue;

            var valueCount = (long)declaredCount;
            var size = unit * valueCount;

            // Values that fit live in the entry itself.
            var fieldAt = offset + countSize + (long)i * entrySize + (cursor.Big ? 12 : 8);
            var valueOffset = size <= fieldSize
                ? fieldAt
                : baseOffset + (cursor.Big ? ClampToLong(UInt64(field, cursor.Little)) : UInt32(field, cursor.Little));

            entries.Add(new TiffEntry(tag, type, valueCount, valueOffset, size));
        }

        if (hasNext)
        {
            var nextField = raw.AsSpan(count * entrySize, fieldSize);
            next = cursor.Big ? ClampToLong(UInt64(nextField, cursor.Little)) : UInt32(nextField, cursor.Little);
        }

        return entries;
    }

    private static List<TiffDirectory> ReadChildren(Cursor cursor, List<TiffEntry> entries, TiffDirectoryKind kind, HashSet<long> visited, int depth, long baseOffset)
    {
        var children = new List<TiffDirectory>();
        foreach (var pointerTag in PointerTags(kind))
        {
            if (entries.FirstOrDefault(e => e.Tag == pointerTag.Tag) is not { Count: > 0 } pointerEntry)
                continue;

            var max = pointerTag.Kind == TiffDirectoryKind.SubImage ? MaxSubIfdsPerDirectory : 1;
            foreach (var pointer in ReadPointers(cursor, pointerEntry, max))
                if (ReadDirectory(cursor, baseOffset + pointer, pointerTag.Kind, visited, depth + 1, out _, baseOffset) is { } child)
                    children.Add(child);
        }

        return children;
    }

    private readonly record struct PointerTag(ushort Tag, TiffDirectoryKind Kind);

    private static readonly PointerTag[] ImagePointers =
    [
        new(TagSubIfds, TiffDirectoryKind.SubImage),
        new(TagExifIfd, TiffDirectoryKind.Exif),
        new(TagGpsIfd, TiffDirectoryKind.Gps)
    ];

    private static readonly PointerTag[] ExifPointers = [new(TagInteropIfd, TiffDirectoryKind.Interop)];

    private static PointerTag[] PointerTags(TiffDirectoryKind kind) => kind switch
    {
        TiffDirectoryKind.Image or TiffDirectoryKind.SubImage => ImagePointers,
        TiffDirectoryKind.Exif => ExifPointers,
        _ => []
    };

    private static List<long> ReadPointers(Cursor cursor, TiffEntry entry, int max)
    {
        var pointers = new List<long>();
        var size = entry.Type switch
        {
            TiffFieldType.Long or TiffFieldType.Ifd => 4,
            TiffFieldType.Long8 or TiffFieldType.Ifd8 => 8,
            _ => 0
        };
        if (size == 0 || !Within(entry, cursor.Length))
            return pointers;

        Span<byte> buffer = stackalloc byte[8];
        for (var i = 0; i < Math.Min(entry.Count, max); i++)
        {
            if (!cursor.ReadAt(entry.ValueOffset + i * (long)size, buffer[..size]))
                continue;

            var pointer = size == 4 ? UInt32(buffer, cursor.Little) : ClampToLong(UInt64(buffer, cursor.Little));
            if (pointer > 0)
                pointers.Add(pointer);
        }

        return pointers;
    }

    private static bool Within(TiffEntry entry, long len) => entry.ValueOffset >= 0 && entry.ValueSize >= 0 && entry.ValueSize <= len && entry.ValueOffset <= len - entry.ValueSize;

    private static int TypeSize(TiffFieldType type) => type switch
    {
        TiffFieldType.Byte or TiffFieldType.Ascii or TiffFieldType.SByte or TiffFieldType.Undefined => 1,
        TiffFieldType.Short or TiffFieldType.SShort => 2,
        TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Float or TiffFieldType.Ifd => 4,
        TiffFieldType.Rational or TiffFieldType.SRational or TiffFieldType.Double => 8,
        TiffFieldType.Long8 or TiffFieldType.SLong8 or TiffFieldType.Ifd8 => 8,
        _ => 0
    };

    private static bool TryByteOrder(ReadOnlySpan<byte> header, out bool little)
    {
        little = header[0] == 'I' && header[1] == 'I';
        return little || (header[0] == 'M' && header[1] == 'M');
    }

    /// <summary>
    /// BigTIFF's unsigned 64-bit offsets and LONG8 values as a long. A plain cast turns anything past
    /// long.MaxValue negative; clamped, a hostile offset stays past the end of every stream, where
    /// the bounds checks already reject it.
    /// </summary>
    private static long ClampToLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;

    private sealed class Cursor(Stream stream, bool little, bool big)
    {
        public long Length { get; } = stream.Length;
        public bool Little { get; } = little;
        public bool Big { get; } = big;

        public bool ReadAt(long offset, Span<byte> buffer) => stream.ReadAt(offset, buffer);
    }
}