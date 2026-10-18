using System.Buffers.Binary;
using System.Text;

namespace Lyra.Metadata.Iso;

public readonly ref struct IsoBox(ReadOnlySpan<byte> type, ReadOnlySpan<byte> body, bool cutShort = false)
{
    public ReadOnlySpan<byte> Type { get; } = type;

    public ReadOnlySpan<byte> Body { get; } = body;

    public bool CutShort { get; } = cutShort;

    public bool Is(ReadOnlySpan<byte> fourCc) => Type.SequenceEqual(fourCc);

    public void Deconstruct(out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> body)
    {
        type = Type;
        body = Body;
    }
}

public ref struct IsoBoxEnumerator(ReadOnlySpan<byte> data, bool keepCutShort = false)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _pos;

    public IsoBox Current { get; private set; }

    public readonly IsoBoxEnumerator GetEnumerator() => this;

    public bool MoveNext()
    {
        if (_pos >= _data.Length)
            return false;

        if (IsoBoxes.Measure(_data[_pos..], _data.Length - _pos) is { } box)
        {
            Current = new IsoBox(_data.Slice(_pos + 4, 4), _data.Slice(_pos + box.HeaderSize, (int)box.BodySize));
            _pos += (int)box.Size;
            return true;
        }

        if (!keepCutShort || IsoBoxes.Measure(_data[_pos..], long.MaxValue) is not { } cut)
            return false;

        Current = new IsoBox(_data.Slice(_pos + 4, 4), _data[(_pos + cut.HeaderSize)..], cutShort: true);
        _pos = _data.Length;
        return true;
    }
}

public readonly record struct IsoBoxHeader(string Type, long Offset, int HeaderSize, long Size, bool CutShort = false)
{
    public long BodyOffset => Offset + HeaderSize;

    public long BodySize => Size - HeaderSize;

    public long End => Offset + Size;
}

/// <summary>
/// The box layout ISO base media files share - HEIF, AVIF, CR3, MP4, JPEG XL and JPEG 2000: a
/// 32-bit size, a type, a 64-bit size when the first is 1, and size 0 for a box that runs to the
/// end. Every size is checked against the space it claims; a box that does not fit ends a walk.
/// </summary>
public static class IsoBoxes
{
    /// <summary>Adobe's UUID for an XMP packet in an "uuid" box, in any ISO-style container.</summary>
    public static ReadOnlySpan<byte> XmpUuid => [0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8, 0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC];

    public static IsoBoxEnumerator Children(ReadOnlySpan<byte> data, bool keepCutShort = false) => new(data, keepCutShort);

    public static bool IsWhole(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        while (offset < data.Length)
        {
            if (Measure(data[offset..], data.Length - offset) is not { } box)
                return false;

            offset += (int)box.Size;
        }

        return true;
    }

    public static bool TryFind(ReadOnlySpan<byte> parent, out ReadOnlySpan<byte> body, params ReadOnlySpan<string> path)
    {
        Span<byte> type = stackalloc byte[4];
        body = parent;

        foreach (var name in path)
        {
            if (Encoding.ASCII.GetBytes(name, type) != 4 || !TryFindChild(body, type, out body))
                return false;
        }

        return true;
    }

    private static bool TryFindChild(ReadOnlySpan<byte> parent, scoped ReadOnlySpan<byte> type, out ReadOnlySpan<byte> body)
    {
        foreach (var child in Children(parent))
        {
            if (child.Is(type))
            {
                body = child.Body;
                return true;
            }
        }

        body = default;
        return false;
    }
    
    public static IsoBoxHeader? ReadHeader(Stream stream, long offset, long end, bool keepCutShort = false)
    {
        end = Math.Min(end, stream.Length);
        if (offset < 0 || end - offset < 8)
            return null;

        Span<byte> header = stackalloc byte[16];
        stream.Position = offset;
        var read = stream.ReadAtLeast(header[..(int)Math.Min(16, end - offset)], 8, throwOnEndOfStream: false);
        if (read < 8)
            return null;

        var type = Encoding.ASCII.GetString(header.Slice(4, 4));
        if (Measure(header[..read], end - offset) is { } box)
            return new IsoBoxHeader(type, offset, box.HeaderSize, box.Size);

        return keepCutShort && Measure(header[..read], long.MaxValue) is { } cut && cut.HeaderSize <= end - offset
            ? new IsoBoxHeader(type, offset, cut.HeaderSize, end - offset, CutShort: true)
            : null;
    }

    /// <summary>The major brand of a file starting with an ftyp box, or null for any other file.</summary>
    public static string? MajorBrand(ReadOnlySpan<byte> head) => head.Length >= 12 && head.Slice(4, 4).SequenceEqual("ftyp"u8) ? Encoding.ASCII.GetString(head.Slice(8, 4)) : null;

    internal readonly record struct Measured(int HeaderSize, long Size)
    {
        public long BodySize => Size - HeaderSize;
    }

    /// <summary>The box <paramref name="header"/> starts, if it fits the <paramref name="room"/> left for it.</summary>
    internal static Measured? Measure(ReadOnlySpan<byte> header, long room)
    {
        if (header.Length < 8)
            return null;

        long size = BinaryPrimitives.ReadUInt32BigEndian(header);
        var headerSize = 8;

        if (size == 1)
        {
            if (header.Length < 16)
                return null;

            var large = BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
            size = large > long.MaxValue ? long.MaxValue : (long)large;
            headerSize = 16;
        }
        else if (size == 0)
        {
            size = room;
        }

        return size < headerSize || size > room ? null : new Measured(headerSize, size);
    }
}