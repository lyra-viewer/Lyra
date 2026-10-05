using System.Buffers.Binary;
using System.IO.Compression;
using Lyra.ManagedCodecs.Raster.Png;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Png;

public class ApngChunkReaderTests
{
    private const int Width = 16, Height = 12;
    private const uint Red = 0xFFFF0000, Blue = 0xFF0000FF;

    [Fact]
    public void TheStructure_IsReadFromTheChunks()
    {
        var png = new ApngBuilder(Width, Height) { Plays = 3 }
            .AddSolid(Red, delayMs: 100)
            .Add(new ApngBuilder.Frame(2, 4, 5, 3, (_, _) => Blue) { DelayNumerator = 3, DelayDenominator = 0, Dispose = 2, Blend = 1 })
            .Build();

        var chunks = ApngChunkReader.Read(png);

        Assert.NotNull(chunks);
        Assert.Equal((Width, Height, (byte)8, (byte)6), (chunks.Width, chunks.Height, chunks.BitDepth, chunks.ColorType));
        Assert.Equal((2, 3), (chunks.DeclaredFrames, chunks.Plays));
        Assert.True(chunks.DefaultIsFrame);
        Assert.False(chunks.Truncated);

        Assert.Equal(
            [
                (0, 0, Width, Height, 100, ApngDispose.None, ApngBlend.Source),
                (2, 4, 5, 3, 30, ApngDispose.Previous, ApngBlend.Over)
            ],
            chunks.Frames.Select(f => (f.X, f.Y, f.Width, f.Height, f.DelayMs, f.Dispose, f.Blend)));

        Assert.All(chunks.Frames, f => Assert.Single(f.Data));
        Assert.True(chunks.Frames.Sum(f => f.EncodedBytes) < png.Length);
    }

    [Fact]
    public void AHiddenDefaultImage_IsNotAFrame()
    {
        var png = new ApngBuilder(Width, Height) { HiddenDefault = (_, _) => Red }.AddSolid(Blue).AddSolid(Red).Build();

        var chunks = ApngChunkReader.Read(png)!;

        Assert.False(chunks.DefaultIsFrame);
        Assert.Equal(2, chunks.Frames.Count);
        Assert.Equal(Blue, Decode(png, chunks, 0)[0]);
    }

    [Fact]
    public void APlainPng_IsNotAnAnimation()
    {
        var png = new ApngBuilder(Width, Height) { Plays = null }.AddSolid(Red).Build();

        Assert.Null(ApngChunkReader.Read(png));
        Assert.False(ApngChunkReader.DeclaresAnimation(new MemoryStream(png)));
    }

    [Fact]
    public void TheAnimationIsFound_PastLargeChunks()
    {
        var png = new ApngBuilder(Width, Height).AddSolid(Red).AddSolid(Blue).Build();
        var withText = InsertAfterIhdr(png, "tEXt", new byte[200_000]);

        Assert.True(ApngChunkReader.DeclaresAnimation(new MemoryStream(withText)));
        Assert.Equal(2, ApngChunkReader.Read(withText)!.Frames.Count);
    }

    [Fact]
    public void OnlyChunksThatChangeDecoding_AreShared()
    {
        var png = new ApngBuilder(Width, Height).AddSolid(Red).Build();
        png = InsertAfterIhdr(png, "tEXt", new byte[40]);
        png = InsertAfterIhdr(png, "gAMA", [0, 0, 0xB1, 0x8F]);

        var shared = Assert.Single(ApngChunkReader.Read(png)!.SharedChunks);

        Assert.Equal("gAMA"u8.ToArray(), png.AsSpan((int)shared.Offset + 4, 4).ToArray());
        Assert.Equal(16, shared.Length);
    }

    [Fact]
    public void ACutFile_KeepsTheStartOfTheFrameItEndsIn()
    {
        var png = new ApngBuilder(Width, Height).AddSolid(Red).AddSolid(Blue).Build();
        var whole = ApngChunkReader.Read(png)!;
        var last = whole.Frames[1].Data[0];

        var cut = ApngChunkReader.Read(png[..(int)(last.Offset + last.Length / 2)])!;

        Assert.True(cut.Truncated);
        Assert.Equal(2, cut.Frames.Count);
        Assert.Equal(last.Length / 2, cut.Frames[1].Data[0].Length);
        Assert.Equal([false, true], cut.Frames.Select(f => f.Cut));
    }

    [Fact]
    public void ACutBeforeAFramesData_LeavesItEmpty()
    {
        var png = new ApngBuilder(Width, Height).AddSolid(Red).AddSolid(Blue).Build();
        var last = ApngChunkReader.Read(png)!.Frames[1].Data[0];

        // Cut inside the fdAT's sequence number.
        var cut = ApngChunkReader.Read(png[..(int)(last.Offset - 2)])!;

        Assert.Empty(cut.Frames[1].Data);
        Assert.True(cut.Frames[1].Cut);
    }

    [Fact]
    public void ACutAfterAWholeFrame_DoesNotCutIt()
    {
        var png = new ApngBuilder(Width, Height).AddSolid(Red).AddSolid(Blue).Build();

        var cut = ApngChunkReader.Read(png[..^6])!;

        Assert.True(cut.Truncated);
        Assert.All(cut.Frames, f => Assert.False(f.Cut));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x8000_0000u)]
    public void ACanvasSizeOutOfRange_IsNotRead(uint width)
    {
        var png = new ApngBuilder(Width, Height).AddSolid(Red).AddSolid(Blue).Build();
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), width);

        Assert.Null(ApngChunkReader.Read(png));
    }

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(0x8000_0001u, 0u)]
    [InlineData(2u, 0x8000_0000u)]
    public void AnAcTLOutOfRange_IsNotAnAnimation(uint frames, uint plays)
    {
        var png = new ApngBuilder(Width, Height).AddSolid(Red).AddSolid(Blue).Build();
        var actl = png.AsSpan(8 + 12 + 13 + 8);
        BinaryPrimitives.WriteUInt32BigEndian(actl, frames);
        BinaryPrimitives.WriteUInt32BigEndian(actl[4..], plays);

        Assert.Null(ApngChunkReader.Read(png));
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void SomethingElse_IsNotRead(byte[] data)
    {
        Assert.Null(ApngChunkReader.Read(data));
        Assert.False(ApngChunkReader.DeclaresAnimation(new MemoryStream(data)));
    }

    [Fact]
    public void AFramesPng_HasItsSizeAndPixels_AndValidCrcs()
    {
        var png = new ApngBuilder(Width, Height)
            .AddSolid(Red)
            .Add(new ApngBuilder.Frame(2, 4, 5, 3, (x, _) => x < 2 ? Red : Blue))
            .Build();

        var chunks = ApngChunkReader.Read(png)!;
        var frame = ApngFramePng.Build(new MemoryStream(png), chunks, chunks.Frames[1]);

        Assert.Equal((5, 3), (BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(20))));

        for (var pos = 8; pos < frame.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(pos));
            var crc = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(pos + 8 + length));
            Assert.Equal(PngCrc.Compute(frame.AsSpan(pos + 4, 4 + length)), crc);
            pos += 12 + length;
        }

        Assert.Equal([Red, Red, Blue, Blue, Blue], Decode(png, chunks, 1)[..5]);
    }

    [Fact]
    public void TheCrc_MatchesTheKnownValue()
    {
        // The IEND chunk's CRC, which every PNG ends with.
        Assert.Equal(0xAE426082u, PngCrc.Compute("IEND"u8));
    }

    /// <summary>The frame's pixels as 0xAARRGGBB, inflated from the frame PNG's IDAT.</summary>
    private static uint[] Decode(byte[] png, ApngChunks chunks, int index)
    {
        var frame = chunks.Frames[index];
        var single = ApngFramePng.Build(new MemoryStream(png), chunks, frame);

        var pos = 8;
        while (!single.AsSpan(pos + 4, 4).SequenceEqual("IDAT"u8))
            pos += 12 + BinaryPrimitives.ReadInt32BigEndian(single.AsSpan(pos));

        var length = BinaryPrimitives.ReadInt32BigEndian(single.AsSpan(pos));
        using var zlib = new ZLibStream(new MemoryStream(single, pos + 8, length), CompressionMode.Decompress);
        var raw = new byte[frame.Height * (1 + frame.Width * 4)];
        zlib.ReadExactly(raw);

        var pixels = new uint[frame.Width * frame.Height];
        for (var y = 0; y < frame.Height; y++)
        for (var x = 0; x < frame.Width; x++)
        {
            var p = y * (1 + frame.Width * 4) + 1 + x * 4;
            pixels[y * frame.Width + x] = (uint)raw[p + 3] << 24 | (uint)raw[p] << 16 | (uint)raw[p + 1] << 8 | raw[p + 2];
        }

        return pixels;
    }

    private static byte[] InsertAfterIhdr(byte[] png, string type, byte[] data)
    {
        using var chunk = new MemoryStream();
        ApngBuilder.WriteChunk(chunk, type, data);

        const int afterIhdr = 8 + 12 + 13;
        return [.. png[..afterIhdr], .. chunk.ToArray(), .. png[afterIhdr..]];
    }
}
