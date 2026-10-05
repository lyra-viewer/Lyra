using Lyra.ManagedCodecs.Raster.Webp;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Webp;

public class WebpChunkReaderTests
{
    private const int Width = 16, Height = 12;

    [Fact]
    public void TheStructure_IsReadFromTheChunks()
    {
        var webp = new WebpBuilder(Width, Height) { LoopCount = 3 }
            .AddFull(WebpBuilder.Vp8lStub(Width, Height))
            .Add(new WebpBuilder.Frame(2, 4, 5, 3, WebpBuilder.Vp8lStub(5, 3)))
            .AddFull(WebpBuilder.Vp8Stub(alpha: true))
            .AddFull(WebpBuilder.Vp8Stub(alpha: false))
            .Build();

        var chunks = WebpChunkReader.Read(webp);

        Assert.NotNull(chunks);
        Assert.Equal(3, chunks.LoopCount);
        Assert.False(chunks.Truncated);

        Assert.Equal([WebpFrameEncoding.Lossless, WebpFrameEncoding.Lossless, WebpFrameEncoding.Lossy, WebpFrameEncoding.Lossy], chunks.Frames.Select(f => f.Encoding));
    }

    [Fact]
    public void AFramesEncodedSize_IsItsWholeChunk()
    {
        // A 5-byte VP8L payload pads to 6: 8 + 16 + 8 + 6.
        var webp = new WebpBuilder(Width, Height).AddFull(WebpBuilder.Vp8lStub(Width, Height)).Build();

        Assert.Equal(38, Assert.Single(WebpChunkReader.Read(webp)!.Frames).EncodedBytes);
    }

    [Fact]
    public void UnknownChunks_AreSkipped_PaddingIncluded()
    {
        var webp = new WebpBuilder(Width, Height)
            .AddFull(WebpBuilder.Vp8Stub(alpha: false))
            .AddChunk("ABCD", [1, 2, 3])
            .AddFull(WebpBuilder.Vp8lStub(Width, Height))
            .Build();

        var chunks = WebpChunkReader.Read(webp)!;

        Assert.Equal([WebpFrameEncoding.Lossy, WebpFrameEncoding.Lossless], chunks.Frames.Select(f => f.Encoding));
        Assert.False(chunks.Truncated);
    }

    [Fact]
    public void AFrameWithoutABitstream_HasNoEncoding()
    {
        var webp = new WebpBuilder(Width, Height).AddFull(WebpBuilder.Chunk("ALPH", [0])).Build();

        Assert.Equal(WebpFrameEncoding.None, Assert.Single(WebpChunkReader.Read(webp)!.Frames).Encoding);
    }

    [Fact]
    public void ACutFile_KeepsTheFramesBeforeTheCut()
    {
        var webp = new WebpBuilder(Width, Height)
            .AddFull(WebpBuilder.Vp8Stub(alpha: false))
            .AddFull(WebpBuilder.Vp8Stub(alpha: false))
            .Build();

        var chunks = WebpChunkReader.Read(webp[..^4])!;

        Assert.True(chunks.Truncated);
        Assert.True(chunks.CutFrame);
        Assert.Single(chunks.Frames);
    }

    [Fact]
    public void ACutOutsideAFrame_IsNotACutFrame()
    {
        var webp = new WebpBuilder(Width, Height)
            .AddFull(WebpBuilder.Vp8Stub(alpha: false))
            .AddChunk("XMP ", new byte[20])
            .Build();

        var chunks = WebpChunkReader.Read(webp[..^4])!;

        Assert.True(chunks.Truncated);
        Assert.False(chunks.CutFrame);
    }

    [Fact]
    public void BytesAfterTheContainer_AreNotReadAsChunks()
    {
        var webp = new WebpBuilder(Width, Height).AddFull(WebpBuilder.Vp8Stub(alpha: false)).Build();
        var extra = WebpBuilder.Chunk("ANMF", new byte[24]);

        var chunks = WebpChunkReader.Read([.. webp, .. extra])!;

        Assert.Single(chunks.Frames);
        Assert.False(chunks.Truncated);
    }

    [Fact]
    public void WithoutAnAnimChunk_TheLoopCountIsUnknown()
    {
        var webp = new WebpBuilder(Width, Height) { LoopCount = null }.AddFull(WebpBuilder.Vp8Stub(alpha: false)).Build();

        Assert.Null(WebpChunkReader.Read(webp)!.LoopCount);
    }

    [Fact]
    public void OnlyAnAnimatedVp8x_DeclaresAnimation()
    {
        var animated = new WebpBuilder(Width, Height).AddFull(WebpBuilder.Vp8Stub(alpha: false)).Build();
        Assert.True(WebpChunkReader.DeclaresAnimation(animated));

        var still = animated.ToArray();
        still[20] &= 0xFD;
        Assert.False(WebpChunkReader.DeclaresAnimation(still));

        byte[] simple = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. WebpBuilder.Vp8lStub(Width, Height), .. new byte[16]];
        Assert.False(WebpChunkReader.DeclaresAnimation(simple));

        Assert.False(WebpChunkReader.DeclaresAnimation(animated.AsSpan(0, WebpChunkReader.HeaderLength - 1)));
    }

    [Theory]
    [InlineData("RIFF\0\0\0\0WAVEVP8X")]
    [InlineData("GIF89a")]
    [InlineData("")]
    public void SomethingElse_IsNotRead(string start)
    {
        var data = new byte[64];
        System.Text.Encoding.ASCII.GetBytes(start).CopyTo(data, 0);

        Assert.Null(WebpChunkReader.Read(data));
    }
}
