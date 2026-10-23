using System.Text;
using Lyra.Metadata.Layout;
using Xunit;
using static Lyra.Metadata.Tests.Support.ChunkFixture;

namespace Lyra.Metadata.Tests.Layout;

/// <summary>PNG chunks, RIFF chunks and GIF blocks: walked in order, a cut one dropped unless asked for.</summary>
public class LayoutTests
{
    [Fact]
    public void PngChunksAreWalkedWholeOrCutShortWhenAsked()
    {
        var png = Png(Ihdr(), Chunk("tEXt", [1, 2, 3, 4, 5]));
        var intoData = png[..^19];
        var intoCrc = png[..^14];

        Assert.Equal(["IHDR", "tEXt", "IEND"], Types(png, keep: false));
        Assert.Equal(["IHDR"], Types(intoData, keep: false));
        Assert.Equal("tEXt 2 10 True", Last(intoData));
        Assert.Equal("tEXt 5 15 True", Last(intoCrc));
    }

    private static string Last(byte[] png)
    {
        var last = "";
        foreach (var chunk in PngChunks.Children(png, keepCutShort: true))
            last = $"{Encoding.ASCII.GetString(chunk.Type)} {chunk.Body.Length} {chunk.TotalBytes} {chunk.CutShort}";

        return last;
    }

    private static List<string> Types(byte[] png, bool keep)
    {
        var types = new List<string>();
        foreach (var chunk in PngChunks.Children(png, keepCutShort: keep))
            types.Add(Encoding.ASCII.GetString(chunk.Type));

        return types;
    }

    [Fact]
    public void RiffChunksStepOverThePadByteAnOddSizeTakes()
    {
        var webp = Webp(RiffChunk("ODD ", [1, 2, 3]), RiffChunk("EVEN", [4, 5]));

        var seen = new List<string>();
        foreach (var chunk in RiffChunks.Children(RiffChunks.Form(webp), start: 12))
            seen.Add($"{Encoding.ASCII.GetString(chunk.FourCc)} {chunk.Offset} {chunk.Next}");

        Assert.Equal(["ODD  12 24", "EVEN 24 34"], seen);
    }

    [Fact]
    public void ARiffChunkCutShortIsKeptOnlyWhenAskedFor()
    {
        var webp = Webp(RiffChunk("WHOL", [1, 2]), RiffChunk("PART", [3, 4, 5, 6]))[..^2];

        var plain = 0;
        foreach (var _ in RiffChunks.Children(webp, start: 12))
            plain++;

        var cut = false;
        foreach (var chunk in RiffChunks.Children(webp, start: 12, keepCutShort: true))
            cut = chunk.CutShort && chunk.Is("PART"u8) && chunk.Body.Length == 2;

        Assert.Equal(1, plain);
        Assert.True(cut);
    }

    [Fact]
    public void GifBlocksEndAtTheTrailerOrWhereTheyBreak()
    {
        var gif = Gif(GifComment("hello"), GifImage);

        Assert.Equal([GifBlockKind.Extension, GifBlockKind.Image, GifBlockKind.Trailer], Kinds(gif));
        Assert.Equal([GifBlockKind.Extension], Kinds(gif[..^8]));
    }

    private static List<GifBlockKind> Kinds(byte[] gif)
    {
        var kinds = new List<GifBlockKind>();
        foreach (var block in GifStream.Children(gif))
            kinds.Add(block.Kind);

        return kinds;
    }

    [Fact]
    public void AGifChainGivesItsFirstSubBlockAndItsDataJoined()
    {
        byte[] chain = [3, 1, 2, 3, 2, 4, 5, 0];

        Assert.Equal([1, 2, 3], GifStream.FirstSubBlock(chain).ToArray());
        Assert.Equal([1, 2, 3, 4, 5], GifStream.Join(chain));
    }

    [Fact]
    public void AnImageBlockSaysWhereItsDataIs()
    {
        var gif = Gif(GifImage);

        foreach (var block in GifStream.Children(gif))
        {
            if (block.Kind != GifBlockKind.Image)
                continue;

            Assert.Equal(13, block.Offset);
            Assert.Equal(13 + 10, block.DataOffset);
            Assert.Equal(gif.Length - 1, block.End);
        }
    }
}