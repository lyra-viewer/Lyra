using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Lyra.ManagedCodecs.Tests.Png;

/// <summary>Writes APNGs for tests: eight-bit RGBA or palette, unfiltered scanlines, zlib from the BCL.</summary>
internal sealed class ApngBuilder(int width, int height)
{
    /// <param name="Pixel">0xAARRGGBB at a point in the frame's own rectangle.</param>
    internal sealed record Frame(int X, int Y, int Width, int Height, Func<int, int, uint> Pixel)
    {
        public ushort DelayNumerator { get; init; } = 1;

        public ushort DelayDenominator { get; init; } = 10;

        /// <summary>0 none, 1 background, 2 previous.</summary>
        public byte Dispose { get; init; }

        /// <summary>0 source, 1 over.</summary>
        public byte Blend { get; init; }
    }

    private readonly List<Frame> _frames = [];

    /// <summary>acTL's play count: 0 forever. Null writes no acTL, leaving a plain PNG.</summary>
    public int? Plays { get; init; } = 0;

    /// <summary>A default image shown by decoders without APNG support, and not part of the animation.</summary>
    public Func<int, int, uint>? HiddenDefault { get; init; }

    /// <summary>Overrides acTL's frame count, for files that misstate it.</summary>
    public int? DeclaredFrames { get; init; }

    /// <summary>0xAARRGGBB entries; when set, the file is indexed color with PLTE and tRNS, and every pixel must be one of them.</summary>
    public uint[]? Palette { get; init; }

    public ApngBuilder Add(Frame frame)
    {
        _frames.Add(frame);
        return this;
    }

    public ApngBuilder AddSolid(uint argb, ushort delayMs = 100) =>
        Add(new Frame(0, 0, width, height, (_, _) => argb) { DelayNumerator = delayMs, DelayDenominator = 1000 });

    public byte[] Build()
    {
        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;
        ihdr[9] = (byte)(Palette is null ? 6 : 3);
        WriteChunk(output, "IHDR", ihdr);

        if (Palette is not null)
        {
            WriteChunk(output, "PLTE", Palette.SelectMany(c => new[] { (byte)(c >> 16), (byte)(c >> 8), (byte)c }).ToArray());
            WriteChunk(output, "tRNS", Palette.Select(c => (byte)(c >> 24)).ToArray());
        }

        if (Plays is { } plays)
        {
            var actl = new byte[8];
            BinaryPrimitives.WriteInt32BigEndian(actl, DeclaredFrames ?? _frames.Count);
            BinaryPrimitives.WriteInt32BigEndian(actl.AsSpan(4), plays);
            WriteChunk(output, "acTL", actl);
        }

        var sequence = 0;

        if (HiddenDefault is not null)
            WriteChunk(output, "IDAT", Compress(width, height, HiddenDefault));

        for (var i = 0; i < _frames.Count; i++)
        {
            var frame = _frames[i];

            if (Plays is not null)
                WriteChunk(output, "fcTL", FrameControl(sequence++, frame));

            var data = Compress(frame.Width, frame.Height, frame.Pixel);

            if (i == 0 && HiddenDefault is null)
                WriteChunk(output, "IDAT", data);
            else
            {
                var fdat = new byte[4 + data.Length];
                BinaryPrimitives.WriteInt32BigEndian(fdat, sequence++);
                data.CopyTo(fdat, 4);
                WriteChunk(output, "fdAT", fdat);
            }
        }

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static byte[] FrameControl(int sequence, Frame frame)
    {
        var fctl = new byte[26];
        BinaryPrimitives.WriteInt32BigEndian(fctl, sequence);
        BinaryPrimitives.WriteInt32BigEndian(fctl.AsSpan(4), frame.Width);
        BinaryPrimitives.WriteInt32BigEndian(fctl.AsSpan(8), frame.Height);
        BinaryPrimitives.WriteInt32BigEndian(fctl.AsSpan(12), frame.X);
        BinaryPrimitives.WriteInt32BigEndian(fctl.AsSpan(16), frame.Y);
        BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(20), frame.DelayNumerator);
        BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(22), frame.DelayDenominator);
        fctl[24] = frame.Dispose;
        fctl[25] = frame.Blend;
        return fctl;
    }

    private byte[] Compress(int width, int height, Func<int, int, uint> pixel)
    {
        var bytesPerPixel = Palette is null ? 4 : 1;
        var raw = new byte[height * (1 + width * bytesPerPixel)];
        var pos = 0;

        for (var y = 0; y < height; y++)
        {
            raw[pos++] = 0;
            for (var x = 0; x < width; x++)
            {
                var argb = pixel(x, y);

                if (Palette is not null)
                {
                    var index = Array.IndexOf(Palette, argb);
                    raw[pos++] = index >= 0 ? (byte)index : throw new ArgumentException($"0x{argb:X8} is not in the palette.");
                    continue;
                }

                raw[pos++] = (byte)(argb >> 16);
                raw[pos++] = (byte)(argb >> 8);
                raw[pos++] = (byte)argb;
                raw[pos++] = (byte)(argb >> 24);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);

        return compressed.ToArray();
    }

    public static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);

        byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
        output.Write(typed);

        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32(typed));
        output.Write(word);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }
}