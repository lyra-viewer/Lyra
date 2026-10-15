using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Raster.Tiff;

/// <summary>One directory entry: where its value lives, not the value itself.</summary>
/// <param name="ValueOffset">Absolute offset of the value bytes in the file.</param>
public readonly record struct TiffEntry(ushort Tag, ushort Type, uint Count, long ValueOffset, long ValueSize);

/// <summary>A directory and the subdirectories its SubIFDs tag points at.</summary>
public sealed class TiffDirectory
{
    public required long Offset { get; init; }
    public required IReadOnlyList<TiffEntry> Entries { get; init; }
    public IReadOnlyList<TiffDirectory> SubDirectories { get; init; } = [];

    public TiffEntry? Find(ushort tag)
    {
        foreach (var entry in Entries)
            if (entry.Tag == tag)
                return entry;

        return null;
    }
}

/// <summary>The directory tree of a classic TIFF, as laid out in the file.</summary>
public sealed class TiffLayout
{
    public required bool LittleEndian { get; init; }
    public required IReadOnlyList<TiffDirectory> Directories { get; init; }

    public IEnumerable<TiffDirectory> AllDirectories()
    {
        foreach (var directory in Directories)
        foreach (var each in WithSubDirectories(directory))
            yield return each;
    }

    private static IEnumerable<TiffDirectory> WithSubDirectories(TiffDirectory directory)
    {
        yield return directory;

        foreach (var sub in directory.SubDirectories)
        foreach (var each in WithSubDirectories(sub))
            yield return each;
    }
}

/// <summary>
/// Walks the directories of a classic TIFF (camera raws among them) from a seekable stream,
/// trusting nothing in the file: every offset is checked against the stream, chains and
/// subdirectory trees are bounded, and a loop ends the walk instead of repeating it.
/// </summary>
public static class TiffDirectoryReader
{
    public const ushort TagSubIfds = 330;

    private const int MaxDirectories = 64;
    private const int MaxEntries = 4096;
    private const int MaxSubIfdDepth = 4;
    private const int MaxSubIfdsPerDirectory = 16;

    /// <summary>The directory tree, or null when the stream is not a classic TIFF.</summary>
    public static TiffLayout? Read(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 8)
            return null;

        Span<byte> header = stackalloc byte[8];
        stream.Position = 0;
        if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8)
            return null;

        bool little;
        if (header[0] == (byte)'I' && header[1] == (byte)'I')
            little = true;
        else if (header[0] == (byte)'M' && header[1] == (byte)'M')
            little = false;
        else
            return null;

        // 42 is TIFF; ORF ("RO", "SR") and RW2 (0x55) use their own magic over the same layout.
        var magic = little ? BinaryPrimitives.ReadUInt16LittleEndian(header[2..]) : BinaryPrimitives.ReadUInt16BigEndian(header[2..]);
        if (magic is not (42 or 0x4F52 or 0x5352 or 0x55))
            return null;

        var cursor = new Cursor(stream, little);
        var visited = new HashSet<long>();
        var directories = new List<TiffDirectory>();

        var next = (long)cursor.U32(header[4..]);
        while (next != 0 && directories.Count < MaxDirectories)
        {
            if (ReadDirectory(cursor, next, visited, depth: 0, out var following) is not { } directory)
                break;

            directories.Add(directory);
            next = following;
        }

        return directories.Count == 0 ? null : new TiffLayout { LittleEndian = little, Directories = directories };
    }

    /// <summary>
    /// One directory at <paramref name="offset"/>, without following its subdirectories. Offsets
    /// stored in it count from <paramref name="baseOffset"/>, which is not 0 in the maker notes of
    /// cameras that treat the note as a file of its own. Null when there is no sane directory there.
    /// </summary>
    public static TiffDirectory? ReadDirectoryAt(Stream stream, long offset, bool littleEndian, out long next, long baseOffset = 0)
    {
        var cursor = new Cursor(stream, littleEndian);
        return ReadDirectory(cursor, offset, [], MaxSubIfdDepth, out next, baseOffset);
    }

    public static long[]? ReadIntegers(Stream stream, TiffEntry entry, bool littleEndian, int maxCount)
    {
        var size = entry.Type switch { 1 or 6 => 1, 3 or 8 => 2, 4 or 9 or 13 => 4, _ => 0 };
        if (size == 0 || entry.Count > maxCount || ReadBytes(stream, entry, maxCount * size) is not { } bytes)
            return null;

        var values = new long[entry.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var b = bytes.AsSpan(i * size, size);
            values[i] = entry.Type switch
            {
                1 => b[0],
                6 => (sbyte)b[0],
                3 => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b),
                8 => littleEndian ? BinaryPrimitives.ReadInt16LittleEndian(b) : BinaryPrimitives.ReadInt16BigEndian(b),
                9 => littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(b) : BinaryPrimitives.ReadInt32BigEndian(b),
                _ => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b)
            };
        }

        return values;
    }

    public static byte[]? ReadBytes(Stream stream, TiffEntry entry, int maxBytes)
    {
        if (entry.ValueSize <= 0 || entry.ValueSize > maxBytes || entry.ValueOffset < 0 || entry.ValueOffset + entry.ValueSize > stream.Length)
            return null;

        var bytes = new byte[entry.ValueSize];
        stream.Position = entry.ValueOffset;
        return stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) == bytes.Length ? bytes : null;
    }

    public static float[]? ReadFloats(Stream stream, TiffEntry entry, bool littleEndian, int maxCount)
    {
        const ushort typeFloat = 11;
        if (entry.Type != typeFloat || entry.Count > maxCount || ReadBytes(stream, entry, maxCount * 4) is not { } bytes)
            return null;

        var values = new float[entry.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var bits = littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * 4)) : BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(i * 4));
            values[i] = BitConverter.Int32BitsToSingle(bits);
        }

        return values;
    }

    private static TiffDirectory? ReadDirectory(Cursor cursor, long offset, HashSet<long> visited, int depth, out long next, long baseOffset = 0)
    {
        next = 0;

        if (offset < 8 || offset + 2 > cursor.Length || !visited.Add(offset))
            return null;

        Span<byte> two = stackalloc byte[2];
        if (!cursor.ReadAt(offset, two))
            return null;

        int count = cursor.U16(two);
        if (count == 0 || count > MaxEntries || offset + 2 + count * 12L + 4 > cursor.Length)
            return null;

        var raw = new byte[count * 12 + 4];
        if (!cursor.ReadAt(offset + 2, raw))
            return null;

        var entries = new List<TiffEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var e = raw.AsSpan(i * 12, 12);
            var tag = cursor.U16(e);
            var type = cursor.U16(e[2..]);
            var valueCount = cursor.U32(e[4..]);

            var size = (long)TypeSize(type) * valueCount;
            if (size == 0)
                continue;

            // Four bytes or fewer live in the entry itself.
            var valueOffset = size <= 4 ? offset + 2 + i * 12L + 8 : baseOffset + cursor.U32(e[8..]);
            entries.Add(new TiffEntry(tag, type, valueCount, valueOffset, size));
        }

        next = cursor.U32(raw.AsSpan(count * 12));

        var subDirectories = new List<TiffDirectory>();
        if (depth < MaxSubIfdDepth && entries.FirstOrDefault(e => e.Tag == TagSubIfds) is { Count: > 0 } subIfds)
        {
            var pointers = ReadPointers(cursor, subIfds, MaxSubIfdsPerDirectory);
            foreach (var pointer in pointers)
                if (ReadDirectory(cursor, baseOffset + pointer, visited, depth + 1, out _, baseOffset) is { } sub)
                    subDirectories.Add(sub);
        }

        return new TiffDirectory { Offset = offset, Entries = entries, SubDirectories = subDirectories };
    }

    private static List<long> ReadPointers(Cursor cursor, TiffEntry entry, int max)
    {
        var pointers = new List<long>();
        if (entry.Type is not (4 or 13) || entry.ValueOffset + entry.ValueSize > cursor.Length)
            return pointers;

        Span<byte> four = stackalloc byte[4];
        for (var i = 0; i < Math.Min(entry.Count, (uint)max); i++)
            if (cursor.ReadAt(entry.ValueOffset + i * 4L, four))
                pointers.Add(cursor.U32(four));

        return pointers;
    }

    /// <summary>Bytes per value of a TIFF field type.</summary>
    private static int TypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,   // BYTE, ASCII, SBYTE, UNDEFINED
        3 or 8 => 2,             // SHORT, SSHORT
        4 or 9 or 11 or 13 => 4, // LONG, SLONG, FLOAT, IFD
        5 or 10 or 12 => 8,      // RATIONAL, SRATIONAL, DOUBLE
        _ => 0
    };

    private sealed class Cursor(Stream stream, bool little)
    {
        public long Length { get; } = stream.Length;

        public bool ReadAt(long offset, Span<byte> buffer)
        {
            if (offset < 0 || offset + buffer.Length > Length)
                return false;

            stream.Position = offset;
            return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
        }

        public ushort U16(ReadOnlySpan<byte> b) => little ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b);

        public uint U32(ReadOnlySpan<byte> b) => little ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b);
    }
}