namespace Lyra.ManagedCodecs.Raster.Gif;

public enum GifPixelDataProblem
{
    None,

    /// <summary>The data ends before the first pixel.</summary>
    NoData,

    /// <summary>The data ends before the frame is full.</summary>
    TooFewPixels,

    /// <summary>A code no valid stream can hold at that point.</summary>
    Corrupt,

    /// <summary>The LZW minimum code size is outside the 0 to 8 a GIF allows.</summary>
    InvalidCodeSize
}

/// <param name="Pixels">Pixels the data yields, counted no further than <paramref name="Expected"/>.</param>
/// <param name="Expected">The pixels the frame's size declares.</param>
/// <param name="CodeSize">The LZW minimum code size, or -1 when there was no data to hold one.</param>
public readonly record struct GifPixelDataCheck(GifPixelDataProblem Problem, long Pixels, long Expected, int CodeSize);

/// <summary>
/// Checks whether a frame's pixel data holds what the frame declares, by running its LZW stream
/// far enough to count the pixels it yields. Tracks only how long each code's string is, never
/// the pixels themselves, so a frame can be judged without being decoded.
/// </summary>
public static class GifPixelData
{
    private const int MaxCodeWidth = 12;
    private const int TableSize = 1 << MaxCodeWidth;

    /// <summary>
    /// Checks <paramref name="data"/>, which starts at the frame's LZW minimum code size byte.
    /// Surplus pixels are no problem: decoders ignore them.
    /// </summary>
    public static GifPixelDataCheck Check(ReadOnlySpan<byte> data, GifFrameBlock frame)
    {
        var expected = (long)frame.Width * frame.Height;

        if (data.IsEmpty)
            return new GifPixelDataCheck(GifPixelDataProblem.NoData, 0, expected, -1);

        int codeSize = data[0];
        if (codeSize > 8)
            return new GifPixelDataCheck(GifPixelDataProblem.InvalidCodeSize, 0, expected, codeSize);

        var (pixels, corrupt) = Count(data[1..], codeSize, expected);

        var problem = corrupt ? GifPixelDataProblem.Corrupt
            : pixels >= expected ? GifPixelDataProblem.None
            : pixels == 0 ? GifPixelDataProblem.NoData
            : GifPixelDataProblem.TooFewPixels;

        return new GifPixelDataCheck(problem, pixels, expected, codeSize);
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
