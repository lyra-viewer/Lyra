using Lyra.Metadata.Iso;
using Xunit;
using static Lyra.Metadata.Tests.Support.IsoFixture;

namespace Lyra.Metadata.Tests.Iso;

/// <summary>Box sizes in all three forms, every one checked against the room it claims.</summary>
public class IsoBoxesTests
{
    private static List<string> Types(ReadOnlySpan<byte> data, out bool broken)
    {
        var types = new List<string>();
        foreach (var box in IsoBoxes.Children(data))
            types.Add(System.Text.Encoding.ASCII.GetString(box.Type));

        broken = !IsoBoxes.IsWhole(data);
        return types;
    }

    [Fact]
    public void BoxesAreWalkedInOrderWithLargeAndToTheEndSizes()
    {
        byte[] large = [0, 0, 0, 1, .. Ascii("larg"), 0, 0, 0, 0, 0, 0, 0, 20, 1, 2, 3, 4];
        byte[] toEnd = [0, 0, 0, 0, .. Ascii("rest"), 9, 9, 9];

        var types = Types([.. Box("one ", [1]), .. large, .. toEnd], out var broken);

        Assert.Equal(["one ", "larg", "rest"], types);
        Assert.False(broken);
    }

    [Fact]
    public void ABoxThatDoesNotFitEndsTheWalkAndSaysSo()
    {
        byte[] lying = [0x7F, 0xFF, 0xFF, 0xFF, .. Ascii("free"), 0];
        byte[] tooSmall = [0, 0, 0, 4, .. Ascii("tiny")];
        byte[] hugeLarge = [0, 0, 0, 1, .. Ascii("larg"), 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

        foreach (var bad in new[] { lying, tooSmall, hugeLarge, [0, 0, 0] })
        {
            var types = Types([.. Box("good", []), .. bad], out var broken);
            Assert.Equal(["good"], types);
            Assert.True(broken);
        }
    }

    [Fact]
    public void ABoxCutShortIsKeptOnlyWhenAskedForAndSaysSo()
    {
        byte[] data = [.. Box("whol", [1]), 0, 0, 0, 20, .. Ascii("part"), 1, 2];

        Assert.Equal(["whol"], Types(data, out _));

        var kept = new List<string>();
        foreach (var box in IsoBoxes.Children(data, keepCutShort: true))
            kept.Add($"{System.Text.Encoding.ASCII.GetString(box.Type)} {box.Body.Length} {box.CutShort}");

        Assert.Equal(["whol 1 False", "part 2 True"], kept);
    }

    [Fact]
    public void APathIsFoundChildByChild()
    {
        var data = Box("moov", Box("trak", Box("tkhd", [7]), Box("mdia", Box("mdhd", [8, 9]))));

        Assert.True(IsoBoxes.TryFind(data, out var mdhd, "moov", "trak", "mdia", "mdhd"));
        Assert.Equal([8, 9], mdhd.ToArray());
        Assert.False(IsoBoxes.TryFind(data, out _, "moov", "mdia"));
    }

    [Fact]
    public void HeadersAreReadFromAStreamWithinTheirBounds()
    {
        using var stream = new MemoryStream([.. Box("ftyp", Ascii("heic")), .. Box("meta", new byte[10]), 0, 0, 0, 0, .. Ascii("mdat"), 1, 2]);

        var ftyp = IsoBoxes.ReadHeader(stream, 0, stream.Length)!.Value;
        var meta = IsoBoxes.ReadHeader(stream, ftyp.End, stream.Length)!.Value;
        var mdat = IsoBoxes.ReadHeader(stream, meta.End, stream.Length)!.Value;

        Assert.Equal(new IsoBoxHeader("meta", 12, 8, 18), meta);
        Assert.Equal(new IsoBoxHeader("mdat", 30, 8, 10), mdat);
        Assert.Null(IsoBoxes.ReadHeader(stream, mdat.End, stream.Length));
        Assert.Null(IsoBoxes.ReadHeader(stream, meta.Offset, meta.End - 1));
    }

    [Fact]
    public void TheMajorBrandComesFromALeadingFtyp()
    {
        Assert.Equal("crx ", IsoBoxes.MajorBrand(Ftyp("crx ")));
        Assert.Null(IsoBoxes.MajorBrand(Box("moov", new byte[8])));
        Assert.Null(IsoBoxes.MajorBrand(Ftyp("heic")[..11]));
    }
}