using System.Buffers.Binary;
using System.Text;

namespace Lyra.ManagedCodecs.Tests.Webp;

internal sealed class WebpBuilder(int width, int height)
{
    internal sealed record Frame(int X, int Y, int Width, int Height, byte[] ImageChunks)
    {
        public int DurationMs { get; init; } = 100;

        public bool Blend { get; init; } = true;

        public bool DisposeToBackground { get; init; }
    }

    /// <summary>Everything after VP8X, in file order.</summary>
    private readonly List<byte[]> _chunks = [];

    /// <summary>ANIM's loop count; null writes no ANIM chunk.</summary>
    public int? LoopCount { get; init; } = 0;

    public bool AlphaFlag { get; init; } = true;

    public WebpBuilder Add(Frame frame)
    {
        var header = new byte[16];
        WriteU24(header, 0, frame.X / 2);
        WriteU24(header, 3, frame.Y / 2);
        WriteU24(header, 6, frame.Width - 1);
        WriteU24(header, 9, frame.Height - 1);
        WriteU24(header, 12, frame.DurationMs);
        header[15] = (byte)((frame.Blend ? 0 : 0x02) | (frame.DisposeToBackground ? 0x01 : 0));

        _chunks.Add(Chunk("ANMF", [.. header, .. frame.ImageChunks]));
        return this;
    }

    /// <summary>A frame covering the whole canvas.</summary>
    public WebpBuilder AddFull(byte[] imageChunks, int durationMs = 100) => Add(new Frame(0, 0, width, height, imageChunks) { DurationMs = durationMs });

    /// <summary>A chunk written as it is, between frames.</summary>
    public WebpBuilder AddChunk(string fourCc, byte[] payload)
    {
        _chunks.Add(Chunk(fourCc, payload));
        return this;
    }

    public byte[] Build()
    {
        var vp8x = new byte[10];
        vp8x[0] = (byte)(0x02 | (AlphaFlag ? 0x10 : 0));
        WriteU24(vp8x, 4, width - 1);
        WriteU24(vp8x, 7, height - 1);

        var anim = LoopCount is { } loop ? Chunk("ANIM", [0, 0, 0, 0, (byte)loop, (byte)(loop >> 8)]) : [];

        byte[] body = [.. "WEBP"u8, .. Chunk("VP8X", vp8x), .. anim, .. _chunks.SelectMany(c => c)];

        var riff = new byte[8];
        "RIFF"u8.CopyTo(riff);
        BinaryPrimitives.WriteUInt32LittleEndian(riff.AsSpan(4), (uint)body.Length);

        return [.. riff, .. body];
    }

    /// <summary>A RIFF chunk: FourCC, little-endian size, payload, and a pad byte to an even length.</summary>
    public static byte[] Chunk(string fourCc, ReadOnlySpan<byte> payload)
    {
        var chunk = new byte[8 + payload.Length + (payload.Length & 1)];
        Encoding.ASCII.GetBytes(fourCc).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(chunk.AsSpan(8));
        return chunk;
    }

    /// <summary>A placeholder VP8L chunk: a valid header for the size, no image data.</summary>
    public static byte[] Vp8lStub(int width, int height)
    {
        var bits = (uint)(width - 1) | (uint)(height - 1) << 14;
        var payload = new byte[5];
        payload[0] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1), bits);
        return Chunk("VP8L", payload);
    }

    /// <summary>Placeholder ALPH and VP8 chunks, or VP8 alone.</summary>
    public static byte[] Vp8Stub(bool alpha) => [.. alpha ? Chunk("ALPH", [0]) : [], .. Chunk("VP8 ", [0, 0, 0])];

    private static void WriteU24(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
    }
}
