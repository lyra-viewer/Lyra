using System.Text;

namespace Lyra.Imaging.Tests.Support;

/// <summary>
/// Writes GIFs for tests. The LZW stream never grows past nine-bit codes - a clear code goes out
/// before the table would widen - so it is valid but not compressed, which is all a fixture needs.
/// </summary>
internal sealed class GifBuilder(int width, int height)
{
    internal sealed record Frame(int Left, int Top, int Width, int Height, Func<int, int, byte> Index)
    {
        public int DelayCentiseconds { get; init; } = 10;

        /// <summary>1 keep, 2 restore to background, 3 restore to previous.</summary>
        public int Disposal { get; init; } = 1;

        public byte? TransparentIndex { get; init; }

        /// <summary>False writes the frame with no graphic control block of its own.</summary>
        public bool HasControl { get; init; } = true;

        public bool Interlaced { get; init; }

        public (byte R, byte G, byte B)[]? LocalPalette { get; init; }
    }

    /// <summary>Everything after the header, in file order.</summary>
    private readonly List<Action<MemoryStream>> _blocks = [];

    public (byte R, byte G, byte B)[] Palette { get; init; } = [(0, 0, 0), (255, 0, 0), (0, 255, 0), (0, 0, 255)];

    /// <summary>The NETSCAPE2.0 repeat count; null writes no extension.</summary>
    public int? LoopCount { get; init; } = 0;

    public List<string> Comments { get; } = [];

    public GifBuilder Add(Frame frame)
    {
        _blocks.Add(output => WriteFrame(output, frame));
        return this;
    }

    /// <summary>Bytes written as they are, between frames, for blocks the builder has no model of.</summary>
    public GifBuilder AddRaw(params byte[] bytes)
    {
        _blocks.Add(output => output.Write(bytes));
        return this;
    }

    /// <summary>A frame covering the whole canvas in one palette index.</summary>
    public GifBuilder AddSolid(byte index, int delayCentiseconds = 10) =>
        Add(new Frame(0, 0, width, height, (_, _) => index) { DelayCentiseconds = delayCentiseconds });

    public byte[] Build()
    {
        using var output = new MemoryStream();

        output.Write("GIF89a"u8);
        WriteU16(output, width);
        WriteU16(output, height);
        output.WriteByte((byte)(Palette.Length > 0 ? 0x80 | PaletteBits(Palette.Length) : 0));
        output.WriteByte(0); // background index
        output.WriteByte(0); // aspect ratio

        WritePalette(output, Palette);

        if (LoopCount is { } loop)
        {
            output.Write([0x21, 0xFF, 11]);
            output.Write("NETSCAPE2.0"u8);
            output.Write([3, 1]);
            WriteU16(output, loop);
            output.WriteByte(0);
        }

        foreach (var comment in Comments)
        {
            output.Write([0x21, 0xFE]);
            WriteSubBlocks(output, Encoding.ASCII.GetBytes(comment));
        }

        foreach (var block in _blocks)
            block(output);

        output.WriteByte(0x3B);
        return output.ToArray();
    }

    private static void WriteFrame(MemoryStream output, Frame frame)
    {
        if (frame.HasControl)
        {
            output.Write([0x21, 0xF9, 4]);
            output.WriteByte((byte)((frame.Disposal << 2) | (frame.TransparentIndex is null ? 0 : 1)));
            WriteU16(output, frame.DelayCentiseconds);
            output.WriteByte(frame.TransparentIndex ?? 0);
            output.WriteByte(0);
        }

        output.WriteByte(0x2C);
        WriteU16(output, frame.Left);
        WriteU16(output, frame.Top);
        WriteU16(output, frame.Width);
        WriteU16(output, frame.Height);

        var flags = frame.Interlaced ? 0x40 : 0;
        if (frame.LocalPalette is { Length: > 0 } local)
            flags |= 0x80 | PaletteBits(local.Length);

        output.WriteByte((byte)flags);

        if (frame.LocalPalette is { Length: > 0 } palette)
            WritePalette(output, palette);

        var indices = new byte[frame.Width * frame.Height];
        var rows = frame.Interlaced ? InterlacedRows(frame.Height) : Enumerable.Range(0, frame.Height);
        var i = 0;

        foreach (var y in rows)
            for (var x = 0; x < frame.Width; x++)
                indices[i++] = frame.Index(x, y);

        const int minCodeSize = 8;
        output.WriteByte(minCodeSize);
        WriteSubBlocks(output, Lzw(indices, minCodeSize));
    }

    private static IEnumerable<int> InterlacedRows(int height)
    {
        foreach (var (start, step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
            for (var y = start; y < height; y += step)
                yield return y;
    }

    private static byte[] Lzw(byte[] indices, int minCodeSize)
    {
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var width = minCodeSize + 1;

        var perRun = (1 << width) - clear - 3;

        var bits = new List<byte>();
        var accumulator = 0;
        var count = 0;

        void Emit(int code)
        {
            accumulator |= code << count;
            count += width;

            while (count >= 8)
            {
                bits.Add((byte)accumulator);
                accumulator >>= 8;
                count -= 8;
            }
        }

        for (var i = 0; i < indices.Length; i++)
        {
            if (i % perRun == 0)
                Emit(clear);

            Emit(indices[i]);
        }

        Emit(end);

        if (count > 0)
            bits.Add((byte)accumulator);

        return [.. bits];
    }

    private static void WriteSubBlocks(MemoryStream output, byte[] data)
    {
        for (var offset = 0; offset < data.Length; offset += 255)
        {
            var size = Math.Min(255, data.Length - offset);
            output.WriteByte((byte)size);
            output.Write(data, offset, size);
        }

        output.WriteByte(0);
    }

    private static void WritePalette(MemoryStream output, (byte R, byte G, byte B)[] palette)
    {
        var size = 1 << (PaletteBits(palette.Length) + 1);
        for (var i = 0; i < size; i++)
        {
            var (r, g, b) = i < palette.Length ? palette[i] : ((byte)0, (byte)0, (byte)0);
            output.Write([r, g, b]);
        }
    }

    private static int PaletteBits(int colors)
    {
        var bits = 0;
        while (1 << (bits + 1) < colors)
            bits++;

        return bits;
    }

    private static void WriteU16(MemoryStream output, int value)
    {
        output.WriteByte((byte)value);
        output.WriteByte((byte)(value >> 8));
    }
}