using System.Buffers.Binary;

namespace Lyra.Metadata.Layout;

/// <summary>One RIFF chunk: its FourCC and data, where it starts and where the next one does.</summary>
/// <param name="next">Where the next chunk starts, past the pad byte an odd size takes - which may lie past the data.</param>
/// <param name="cutShort">The data ends inside the chunk; <see cref="Body"/> is what there is.</param>
public readonly ref struct RiffChunk(ReadOnlySpan<byte> fourCc, ReadOnlySpan<byte> body, int offset, long next, bool cutShort)
{
    public ReadOnlySpan<byte> FourCc { get; } = fourCc;

    public ReadOnlySpan<byte> Body { get; } = body;

    public int Offset { get; } = offset;

    public long Next { get; } = next;

    public bool CutShort { get; } = cutShort;

    public bool Is(ReadOnlySpan<byte> fourCc) => FourCc.SequenceEqual(fourCc);
}

/// <summary>
/// The chunks inside a span from <paramref name="start"/>, stopping at the first the span does not
/// hold whole - or, when <paramref name="keepCutShort"/>, after yielding that one as far as it goes.
/// </summary>
public ref struct RiffChunkEnumerator(ReadOnlySpan<byte> data, int start, bool keepCutShort)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private long _pos = start;
    private bool _done;

    public RiffChunk Current { get; private set; }

    public readonly RiffChunkEnumerator GetEnumerator() => this;

    public bool MoveNext()
    {
        if (_done || _pos < 0 || _pos + 8 > _data.Length)
            return false;

        var at = (int)_pos;
        var fourCc = _data.Slice(at, 4);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(_data[(at + 4)..]);

        if (size > (uint)(_data.Length - at - 8))
        {
            _done = true;
            if (!keepCutShort)
                return false;

            Current = new RiffChunk(fourCc, _data[(at + 8)..], at, _data.Length, cutShort: true);
            return true;
        }

        _pos = at + 8L + size + (size & 1);
        Current = new RiffChunk(fourCc, _data.Slice(at + 8, (int)size), at, _pos, cutShort: false);
        return true;
    }
}

/// <summary>RIFF's layout, as WebP uses it: a FourCC, a little-endian size, the data, and a pad byte after an odd size.</summary>
public static class RiffChunks
{ 
    public static ReadOnlySpan<byte> Form(ReadOnlySpan<byte> data) => data.Length < 8 ? [] : data[..(int)Math.Min(data.Length, 8L + BinaryPrimitives.ReadUInt32LittleEndian(data[4..]))];

    public static RiffChunkEnumerator Children(ReadOnlySpan<byte> data, int start, bool keepCutShort = false) => new(data, start, keepCutShort);
}