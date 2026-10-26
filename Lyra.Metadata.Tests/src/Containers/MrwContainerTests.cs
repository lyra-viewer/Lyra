using Lyra.Metadata.Containers;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Tests.Support;
using Xunit;

namespace Lyra.Metadata.Tests.Containers;

public class MrwContainerTests
{
    private static readonly byte[] Tiff = TiffFixture.Build([TiffFixture.Ascii(ExifTags.Make, "MINOLTA")]);

    private static byte[]? TtwOf(Stream stream) => MrwContainer.Read(stream)?.Blocks.SingleOrDefault()?.Data;

    [Fact]
    public void TheBlocksAndWhereTheRawDataStartsAreRead()
    {
        var file = MrwFixture.Build(Tiff, leading: [MrwFixture.Block("PRD", new byte[24])]);

        var layout = MrwContainer.ReadLayout(new MemoryStream(file))!;

        Assert.Equal(["PRD", "TTW"], layout.Blocks.Select(block => block.Tag));
        Assert.Equal(8, layout.Blocks[0].Offset);
        Assert.Equal(24, layout.Blocks[0].DataLength);
        Assert.Equal(8 + 8 + 24, layout.Blocks[1].Offset);
        Assert.Equal(file.Length - 64, layout.DataOffset);
    }

    [Fact]
    public void TheTtwBlockIsFoundBehindOtherBlocksAndThePositionIsKept()
    {
        var stream = new MemoryStream(MrwFixture.Build(Tiff, leading: [MrwFixture.Block("PRD", new byte[24])])) { Position = 3 };

        Assert.Equal(Tiff, TtwOf(stream));
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void ATtwBlockPastTheEndYieldsNothing()
    {
        var file = MrwFixture.Build(Tiff);

        Assert.Null(TtwOf(new MemoryStream(file[..^(64 + 4)])));
    }

    [Fact]
    public void ATiffIsNotAnMrw()
    {
        Assert.Null(MrwContainer.ReadLayout(new MemoryStream(Tiff)));
        Assert.Null(TtwOf(new MemoryStream(Tiff)));
    }

    [Fact]
    public void DamagedFilesNeverThrow()
    {
        var file = MrwFixture.Build(Tiff, leading: [MrwFixture.Block("PRD", new byte[24]), MrwFixture.Block("WBG", new byte[12])]);
        var random = new Random(8);

        for (var variant = 0; variant < 200; variant++)
        {
            var damaged = variant % 3 == 0 ? file[..random.Next(file.Length)] : (byte[])file.Clone();
            if (variant % 3 != 0)
                for (var i = 0; i < 1 + variant % 5; i++)
                    damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));

            _ = TtwOf(new MemoryStream(damaged));
        }
    }
}