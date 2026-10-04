using Lyra.ManagedCodecs.Raster.Gif;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Gif;

public class GifBlockReaderTests
{
    private const byte Red = 1, Green = 2, Blue = 3;
    private const int Width = 16, Height = 12;

    [Fact]
    public void TheStructure_IsReadFromTheBlocks()
    {
        var builder = new GifBuilder(Width, Height) { LoopCount = 3 };
        builder.Comments.Add("made by a test");

        var gif = builder
            .AddSolid(Red)
            .Add(new GifBuilder.Frame(2, 3, 4, 5, (_, _) => 1) { Interlaced = true, LocalPalette = [(1, 2, 3), (4, 5, 6)] })
            .Add(new GifBuilder.Frame(0, 0, 2, 2, (_, _) => 0) { TransparentIndex = 0 })
            .Build();

        var blocks = GifBlockReader.Read(gif);

        Assert.NotNull(blocks);
        Assert.Equal("GIF89a", blocks.Version);
        Assert.Equal(4, blocks.GlobalPaletteSize);
        Assert.Equal(3, blocks.LoopCount);
        Assert.Equal(1, blocks.CommentCount);
        Assert.False(blocks.Truncated);

        Assert.Equal(3, blocks.Frames.Count);
        Assert.Equal((4, 5), (blocks.Frames[1].Width, blocks.Frames[1].Height));
        Assert.True(blocks.Frames[1].Interlaced);
        Assert.Equal(2, blocks.Frames[1].LocalPaletteSize);
        Assert.False(blocks.Frames[0].Transparent);
        Assert.True(blocks.Frames[2].Transparent);

        Assert.All(blocks.Frames, frame => Assert.True(frame.EncodedBytes > 10));
        Assert.True(blocks.Frames.Sum(f => f.EncodedBytes) < gif.Length);
    }

    [Fact]
    public void ATextBlocksTransparency_IsLeftToTheText()
    {
        var gif = new GifBuilder(Width, Height)
            .AddRaw(0x21, 0xF9, 4, 0x01, 0, 0, 0, 0)
            .AddRaw([0x21, 0x01, 12, .. new byte[12], 2, (byte)'h', (byte)'i', 0])
            .Add(new GifBuilder.Frame(0, 0, Width, Height, (_, _) => Red) { HasControl = false })
            .Build();

        Assert.False(Assert.Single(GifBlockReader.Read(gif)!.Frames).Transparent);
    }

    [Fact]
    public void WhatIsNotAGif_IsRefused()
    {
        Assert.Null(GifBlockReader.Read("not a gif at all"u8));
        Assert.Null(GifBlockReader.Read("GIF89a"u8));
        Assert.Null(GifBlockReader.Read([]));
    }

    [Fact]
    public void AFileCutShort_KeepsTheWholeFrames()
    {
        var gif = new GifBuilder(Width, Height).AddSolid(Red).AddSolid(Green).AddSolid(Blue).Build();
        var whole = GifBlockReader.Read(gif)!;

        var blocks = GifBlockReader.Read(gif.AsSpan(0, gif.Length - (int)whole.Frames[2].EncodedBytes / 2));

        Assert.NotNull(blocks);
        Assert.True(blocks.Truncated);
        Assert.Equal(2, blocks.Frames.Count);
    }

    [Fact]
    public void AFramesPixelData_IsLocated()
    {
        var gif = new GifBuilder(Width, Height).AddSolid(Red).Build();
        var frame = Assert.Single(GifBlockReader.Read(gif)!.Frames);

        // Starts at the LZW minimum code size and ends on the sub-block terminator.
        Assert.Equal(8, gif[frame.DataOffset]);
        Assert.Equal(0, gif[frame.DataOffset + frame.DataLength - 1]);
        Assert.Equal(0x3B, gif[frame.DataOffset + frame.DataLength]);
    }
}
