using System.Buffers.Binary;

namespace Lyra.Metadata.Layout;

/// <summary>One PNG chunk: its type and data, where it starts, and - when cut short - what of its data there is.</summary>
/// <param name="offset">Where the chunk's length field is.</param>
/// <param name="totalBytes">The bytes the chunk takes in the file - length, type, data and CRC - or, cut short, as many as there are.</param>
/// <param name="cutShort">The data ends inside the chunk; <see cref="Body"/> is what there is of its data.</param>
public readonly ref struct PngChunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> body, int offset, int totalBytes, bool cutShort)
{
    public ReadOnlySpan<byte> Type { get; } = type;

    public ReadOnlySpan<byte> Body { get; } = body;

    public int Offset { get; } = offset;

    public bool CutShort { get; } = cutShort;

    public int TotalBytes { get; } = totalBytes;

    public bool Is(ReadOnlySpan<byte> fourCc) => Type.SequenceEqual(fourCc);
}

/// <summary>
/// The chunks of a PNG from <paramref name="start"/>, stopping at the first the data does not hold
/// whole - or, when <paramref name="keepCutShort"/>, after yielding that one as far as it goes.
/// </summary>
public ref struct PngChunkEnumerator(ReadOnlySpan<byte> data, int start, bool keepCutShort)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _pos = start;
    private bool _done;

    public PngChunk Current { get; private set; }

    public readonly PngChunkEnumerator GetEnumerator() => this;

    public bool MoveNext()
    {
        if (_done || _pos < 0 || _pos > _data.Length - 8)
            return false;

        var length = BinaryPrimitives.ReadUInt32BigEndian(_data[_pos..]);
        var type = _data.Slice(_pos + 4, 4);
        var body = _pos + 8;

        if (length + 4L > _data.Length - body)
        {
            _done = true;
            if (!keepCutShort)
                return false;

            Current = new PngChunk(type, _data.Slice(body, (int)Math.Min(length, _data.Length - body)), _pos, _data.Length - _pos, cutShort: true);
            return true;
        }

        Current = new PngChunk(type, _data.Slice(body, (int)length), _pos, 12 + (int)length, cutShort: false);
        _pos = body + (int)length + 4;
        return true;
    }
}

/// <summary>The chunk layout of PNG and APNG: a signature, then length, type, data and CRC, over and over.</summary>
public static class PngChunks
{
    public static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static PngChunkEnumerator Children(ReadOnlySpan<byte> data, int start = 8, bool keepCutShort = false) => new(data, start, keepCutShort);
}