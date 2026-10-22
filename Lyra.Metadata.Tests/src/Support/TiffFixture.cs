using System.Buffers.Binary;
using System.Text;
using Lyra.Metadata.Tiff;

namespace Lyra.Metadata.Tests.Support;

/// <summary>
/// Writes TIFFs and BigTIFFs in either byte order from a description of their directories, with
/// EXIF, GPS, Interop and SubIFD directories hanging off pointer tags.
/// </summary>
internal static class TiffFixture
{
    internal sealed record Entry(ushort Tag, ushort Type, long Count, Func<bool, byte[]> Encode, IReadOnlyList<Entry[]>? Children = null);

    public static Entry Ascii(ushort tag, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        return new Entry(tag, 2, bytes.Length, _ => bytes);
    }

    public static Entry Bytes(ushort tag, params byte[] values) => new(tag, 1, values.Length, _ => values);

    public static Entry Undefined(ushort tag, params byte[] values) => new(tag, 7, values.Length, _ => values);

    public static Entry Short(ushort tag, params ushort[] values) =>
        new(tag, 3, values.Length, little => Concat(values, 2, (span, v) => Write16(span, little, v)));

    public static Entry Long(ushort tag, params uint[] values) =>
        new(tag, 4, values.Length, little => Concat(values, 4, (span, v) => Write32(span, little, v)));

    public static Entry Rational(ushort tag, params TiffRational[] values) =>
        new(tag, 5, values.Length, little => Concat(values, 8, (span, v) =>
        {
            Write32(span, little, (uint)v.Numerator);
            Write32(span[4..], little, (uint)v.Denominator);
        }));

    public static Entry SignedRational(ushort tag, params TiffRational[] values) =>
        new(tag, 10, values.Length, little => Concat(values, 8, (span, v) =>
        {
            Write32(span, little, (uint)(int)v.Numerator);
            Write32(span[4..], little, (uint)(int)v.Denominator);
        }));

    public static Entry Double(ushort tag, params double[] values) =>
        new(tag, 12, values.Length, little => Concat(values, 8, (span, v) => Write64(span, little, (ulong)BitConverter.DoubleToInt64Bits(v))));

    /// <summary>A pointer to one directory, as EXIF, GPS and Interop use, of the layout's natural type.</summary>
    public static Entry Pointer(ushort tag, params Entry[] child) => new(tag, 0, 1, _ => [], [child]);

    /// <summary>A pointer of the given type: LONG (4), IFD (13), LONG8 (16) or IFD8 (18).</summary>
    public static Entry Pointer(ushort tag, ushort type, params Entry[] child) => new(tag, type, 1, _ => [], [child]);

    public static Entry SubIfds(params Entry[][] children) => new(TiffDirectoryReader.TagSubIfds, 0, children.Length, _ => [], children);

    public static TiffRational R(long numerator, long denominator = 1) => new(numerator, denominator);

    /// <summary>The file: the first directory is IFD0 and the rest follow it in the chain.</summary>
    public static byte[] Build(bool little, bool big, params Entry[][] chain)
    {
        var file = new List<byte>();

        file.AddRange(little ? "II"u8.ToArray() : "MM"u8.ToArray());
        file.AddRange(U16(little, big ? (ushort)43 : (ushort)42));
        if (big)
        {
            file.AddRange(U16(little, 8));
            file.AddRange(U16(little, 0));
        }

        var firstPointerAt = file.Count;
        file.AddRange(new byte[big ? 8 : 4]);

        var previousNextAt = firstPointerAt;
        foreach (var directory in chain)
        {
            var at = WriteDirectory(file, directory, little, big, out var nextAt);
            Patch(file, previousNextAt, at, little, big);
            previousNextAt = nextAt;
        }

        return file.ToArray();
    }

    public static byte[] Build(params Entry[][] chain) => Build(little: true, big: false, chain);

    /// <summary>Writes the directory's values and children first, then the directory; returns where it starts.</summary>
    private static long WriteDirectory(List<byte> file, Entry[] entries, bool little, bool big, out int nextAt)
    {
        var fieldSize = big ? 8 : 4;
        var encoded = new List<Encoded>();

        foreach (var entry in entries.OrderBy(e => e.Tag))
        {
            if (entry.Children is { } children)
            {
                var pointers = children.Select(child => WriteDirectory(file, child, little, big, out _)).ToList();
                var type = entry.Type != 0 ? entry.Type : big ? (ushort)16 : (ushort)4;
                var size = type is 16 or 18 ? 8 : 4;

                var values = new byte[pointers.Count * size];
                for (var i = 0; i < pointers.Count; i++)
                    WriteOffset(values.AsSpan(i * size), little, size == 8, pointers[i]);

                encoded.Add(new Encoded(entry.Tag, type, pointers.Count, Place(file, values, fieldSize, little, big)));
                continue;
            }

            encoded.Add(new Encoded(entry.Tag, entry.Type, entry.Count, Place(file, entry.Encode(little), fieldSize, little, big)));
        }

        Align(file);
        var at = (long)file.Count;

        if (big)
            file.AddRange(U64(little, (ulong)encoded.Count));
        else
            file.AddRange(U16(little, (ushort)encoded.Count));

        foreach (var e in encoded)
        {
            file.AddRange(U16(little, e.Tag));
            file.AddRange(U16(little, e.Type));
            file.AddRange(big ? U64(little, (ulong)e.Count) : U32(little, (uint)e.Count));
            file.AddRange(e.Field);
        }

        nextAt = file.Count;
        file.AddRange(new byte[fieldSize]);

        return at;
    }

    private readonly record struct Encoded(ushort Tag, ushort Type, long Count, byte[] Field);

    private static byte[] Place(List<byte> file, byte[] value, int fieldSize, bool little, bool big)
    {
        if (value.Length <= fieldSize)
        {
            var field = new byte[fieldSize];
            value.CopyTo(field, 0);
            return field;
        }

        Align(file);
        var at = file.Count;
        file.AddRange(value);

        var pointer = new byte[fieldSize];
        WriteOffset(pointer, little, big, at);
        return pointer;
    }

    private static void Patch(List<byte> file, int at, long value, bool little, bool big)
    {
        var bytes = new byte[big ? 8 : 4];
        WriteOffset(bytes, little, big, value);
        for (var i = 0; i < bytes.Length; i++)
            file[at + i] = bytes[i];
    }

    private static void Align(List<byte> file)
    {
        if (file.Count % 2 != 0)
            file.Add(0);
    }

    private static void WriteOffset(Span<byte> span, bool little, bool big, long value)
    {
        if (big)
            Write64(span, little, (ulong)value);
        else
            Write32(span, little, (uint)value);
    }

    private static byte[] Concat<T>(T[] values, int size, SpanAction<T> write)
    {
        var bytes = new byte[values.Length * size];
        for (var i = 0; i < values.Length; i++)
            write(bytes.AsSpan(i * size, size), values[i]);

        return bytes;
    }

    private delegate void SpanAction<in T>(Span<byte> span, T value);

    private static byte[] U16(bool little, ushort value)
    {
        var bytes = new byte[2];
        Write16(bytes, little, value);
        return bytes;
    }

    private static byte[] U32(bool little, uint value)
    {
        var bytes = new byte[4];
        Write32(bytes, little, value);
        return bytes;
    }

    private static byte[] U64(bool little, ulong value)
    {
        var bytes = new byte[8];
        Write64(bytes, little, value);
        return bytes;
    }

    private static void Write16(Span<byte> span, bool little, ushort value)
    {
        if (little) BinaryPrimitives.WriteUInt16LittleEndian(span, value);
        else BinaryPrimitives.WriteUInt16BigEndian(span, value);
    }

    private static void Write32(Span<byte> span, bool little, uint value)
    {
        if (little) BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        else BinaryPrimitives.WriteUInt32BigEndian(span, value);
    }

    private static void Write64(Span<byte> span, bool little, ulong value)
    {
        if (little) BinaryPrimitives.WriteUInt64LittleEndian(span, value);
        else BinaryPrimitives.WriteUInt64BigEndian(span, value);
    }
}