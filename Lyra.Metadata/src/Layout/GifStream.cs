namespace Lyra.Metadata.Layout;

public enum GifBlockKind
{
    Extension,
    Image,
    Trailer
}

/// <summary>One block of a GIF's stream, from its introducer on.</summary>
/// <param name="label">An extension's label; 0 for any other block.</param>
/// <param name="descriptor">An image's 9-byte descriptor, after the separator; empty for any other block.</param>
/// <param name="subBlocks">The sub-block chain, its terminator included: an extension's, or an image's data.</param>
/// <param name="offset">Where the introducer is.</param>
/// <param name="end">Where the next block starts.</param>
/// <param name="dataOffset">An image's LZW code size byte, which starts its data.</param>
public readonly ref struct GifBlock(GifBlockKind kind, byte label, ReadOnlySpan<byte> descriptor, ReadOnlySpan<byte> subBlocks, int offset, int end, int dataOffset)
{
    public GifBlockKind Kind { get; } = kind;

    public byte Label { get; } = label;

    public ReadOnlySpan<byte> Descriptor { get; } = descriptor;

    public ReadOnlySpan<byte> SubBlocks { get; } = subBlocks;

    public int Offset { get; } = offset;

    public int End { get; } = end;

    public int DataOffset { get; } = dataOffset;
}

/// <summary>The blocks of a GIF's stream, up to its trailer or the first block it does not hold whole.</summary>
public ref struct GifBlockEnumerator(ReadOnlySpan<byte> data, int start)
{
    private const byte ExtensionIntroducer = 0x21;
    private const byte ImageSeparator = 0x2C;
    private const byte TrailerByte = 0x3B;

    private readonly ReadOnlySpan<byte> _data = data;
    private int _pos = start;
    private bool _done;

    public GifBlock Current { get; private set; }

    public readonly GifBlockEnumerator GetEnumerator() => this;

    public bool MoveNext()
    {
        if (_done || _pos < 0 || _pos >= _data.Length)
            return false;

        var offset = _pos;
        var introducer = _data[_pos++];

        switch (introducer)
        {
            case TrailerByte:
                _done = true;
                Current = new GifBlock(GifBlockKind.Trailer, 0, [], [], offset, _pos, 0);
                return true;

            case ExtensionIntroducer when _pos < _data.Length:
            {
                var label = _data[_pos++];
                var chain = _pos;
                if (!GifStream.SkipSubBlocks(_data, ref _pos))
                    break;

                Current = new GifBlock(GifBlockKind.Extension, label, [], _data[chain.._pos], offset, _pos, 0);
                return true;
            }

            case ImageSeparator when _pos + 9 <= _data.Length:
            {
                var descriptor = _data.Slice(_pos, 9);
                _pos += 9 + GifStream.PaletteBytes(descriptor[8]);

                var dataOffset = _pos++;
                var chain = _pos;
                if (_pos > _data.Length || !GifStream.SkipSubBlocks(_data, ref _pos))
                    break;

                Current = new GifBlock(GifBlockKind.Image, 0, descriptor, _data[chain.._pos], offset, _pos, dataOffset);
                return true;
            }
        }

        _done = true;
        return false;
    }
}

/// <summary>GIF's layout: a header and logical screen, an optional global palette, then blocks to a trailer.</summary>
public static class GifStream
{
    public static bool IsGif(ReadOnlySpan<byte> head) => head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8);

    /// <summary>The colors of the global palette the logical screen declares; 0 without one.</summary>
    public static int GlobalPaletteSize(ReadOnlySpan<byte> data) => (data[10] & 0x80) != 0 ? 1 << ((data[10] & 0x07) + 1) : 0;

    /// <summary>The blocks after the header, logical screen and global palette; the data must hold the 13-byte header.</summary>
    public static GifBlockEnumerator Children(ReadOnlySpan<byte> data) => new(data, 13 + 3 * GlobalPaletteSize(data));

    /// <summary>The bytes a palette takes, from the packed flags that declare it.</summary>
    internal static int PaletteBytes(byte flags) => (flags & 0x80) != 0 ? 3 * (1 << ((flags & 0x07) + 1)) : 0;

    /// <summary>The first sub-block's data, or nothing when the chain does not hold it.</summary>
    public static ReadOnlySpan<byte> FirstSubBlock(ReadOnlySpan<byte> chain)
    {
        if (chain.IsEmpty)
            return [];

        var size = chain[0];
        return 1 + size <= chain.Length ? chain.Slice(1, size) : [];
    }

    /// <summary>The data of a chain's sub-blocks, joined, terminator dropped.</summary>
    public static byte[] Join(ReadOnlySpan<byte> chain)
    {
        var joined = new List<byte>(chain.Length);
        var pos = 0;
        while (pos < chain.Length && chain[pos] != 0)
        {
            var size = chain[pos];
            joined.AddRange(chain.Slice(pos + 1, Math.Min(size, chain.Length - pos - 1)));
            pos += 1 + size;
        }

        return [.. joined];
    }

    internal static bool SkipSubBlocks(ReadOnlySpan<byte> data, ref int pos)
    {
        while (pos < data.Length)
        {
            var size = data[pos++];
            if (size == 0)
                return true;

            pos += size;
        }

        return false;
    }
}