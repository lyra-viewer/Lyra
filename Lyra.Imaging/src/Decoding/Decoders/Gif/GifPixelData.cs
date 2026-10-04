namespace Lyra.Imaging.Decoding.Decoders.Gif;

/// <summary>
/// Says why a frame's pixel data would not decode, by running its LZW stream far enough to count
/// the pixels it yields. Tracks only how long each code's string is, never the pixels themselves.
/// </summary>
internal static class GifPixelData
{
    private const int MaxCodeWidth = 12;
    private const int TableSize = 1 << MaxCodeWidth;

    /// <summary>
    /// The reason frame <paramref name="index"/> cannot be decoded, or null when its data holds
    /// every pixel it declares. <paramref name="data"/> starts at the LZW minimum code size byte.
    /// </summary>
    public static string? Explain(ReadOnlySpan<byte> data, GifFrameBlock frame, int index)
    {
        var name = $"Frame {index + 1}";
        var expected = (long)frame.Width * frame.Height;

        if (data.IsEmpty)
            return $"{name} has no pixel data";

        int minCodeSize = data[0];
        if (minCodeSize > 8)
            return $"{name} declares an invalid LZW code size ({minCodeSize})";

        var (pixels, corrupt) = Count(data[1..], minCodeSize, expected);

        if (corrupt)
            return $"{name}'s pixel data is corrupt after {pixels} of {expected} pixels";

        if (pixels >= expected)
            return null;

        return pixels == 0 ? $"{name} has no pixel data" : $"{name} holds {pixels} of its {expected} pixels";
    }

    /// <summary>Pixels the stream yields, up to <paramref name="enough"/>, and whether it hit an impossible code.</summary>
    private static (long Pixels, bool Corrupt) Count(ReadOnlySpan<byte> subBlocks, int minCodeSize, long enough)
    {
        var clear = 1 << minCodeSize;
        var end = clear + 1;

        Span<ushort> lengths = stackalloc ushort[TableSize];
        var width = minCodeSize + 1;
        var next = end + 1;
        var prev = -1;
        long pixels = 0;

        var reader = new SubBlockBits(subBlocks);

        while (pixels < enough && reader.TryRead(width, out var code))
        {
            if (code == clear)
            {
                width = minCodeSize + 1;
                next = end + 1;
                prev = -1;
                continue;
            }

            if (code == end)
                break;

            int length;
            if (code < clear)
                length = 1;
            else if (prev >= 0 && code < next)
                length = lengths[code];
            else if (prev >= 0 && code == next)
                length = (prev < clear ? 1 : lengths[prev]) + 1;
            else
                return (pixels, true);

            pixels += length;

            if (prev >= 0 && next < TableSize)
            {
                lengths[next] = (ushort)((prev < clear ? 1 : lengths[prev]) + 1);
                next++;

                if (next == 1 << width && width < MaxCodeWidth)
                    width++;
            }

            prev = code;
        }

        return (pixels, false);
    }

    /// <summary>Reads LSB-first codes across a chain of sub-blocks, ending at the terminator or the data.</summary>
    private ref struct SubBlockBits(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _pos;
        private int _remaining;
        private uint _bits;
        private int _count;
        private bool _ended;

        public bool TryRead(int width, out int code)
        {
            while (_count < width)
            {
                if (!TryNextByte(out var b))
                {
                    code = 0;
                    return false;
                }

                _bits |= (uint)b << _count;
                _count += 8;
            }

            code = (int)(_bits & ((1u << width) - 1));
            _bits >>= width;
            _count -= width;
            return true;
        }

        private bool TryNextByte(out byte value)
        {
            value = 0;

            while (_remaining == 0)
            {
                if (_ended || _pos >= _data.Length)
                    return false;

                _remaining = _data[_pos++];
                if (_remaining == 0)
                {
                    _ended = true;
                    return false;
                }
            }

            if (_pos >= _data.Length)
                return false;

            value = _data[_pos++];
            _remaining--;
            return true;
        }
    }
}
