using Lyra.Metadata.Containers;
using Lyra.Metadata.Photoshop;
using Lyra.Metadata.Tests.Support;
using Xunit;
using static Lyra.Metadata.Tests.Support.BitmapFixture;

namespace Lyra.Metadata.Tests.Containers;

public class BitmapContainerTests
{
    private static readonly byte[] Profile = IccFixture.Profile("RGB ", 4, IccFixture.Desc("Embedded"));

    [Theory]
    [InlineData(12, 24, null)]
    [InlineData(16, 8, null)]
    [InlineData(64, 4, 3)]
    [InlineData(40, 32, 3)]
    [InlineData(52, 16, 0)]
    [InlineData(56, 16, 6)]
    [InlineData(108, 32, 3)]
    [InlineData(124, 32, 0)]
    public void EachHeaderLayoutGivesItsBitsAndCompression(int size, int bits, int? compression)
    {
        var container = BmpContainer.Read(Bmp(size, (ushort)bits, compression ?? 0))!;

        Assert.Equal(new BmpHeader(size, bits, compression), container.Fact<BmpFacts>()!.Headers.Single());
        Assert.Empty(container.Blocks);
    }

    [Fact]
    public void AV5HeadersEmbeddedProfileIsItsIccButALinkedOneIsNot()
    {
        Assert.Equal(Profile, BmpContainer.Read(Bmp(124, 24, profile: Profile))!.Blocks.Single().Data);
        Assert.Empty(BmpContainer.Read(Bmp(124, 24, profile: "C:\\profile.icc\0"u8.ToArray(), linked: true))!.Blocks);
    }

    [Fact]
    public void ABitmapArrayGivesEveryHeader()
    {
        var array = BitmapArray(Bmp(12, 1, magic: "CI"), Bmp(40, 24), Bmp(12, 8, magic: "IC"));

        Assert.Equal([1, 24, 8], BmpContainer.Read(array)!.Fact<BmpFacts>()!.Headers.Select(header => header.BitsPerPixel));
    }

    [Fact]
    public void ABmpCutShortKeepsWhatItRead()
    {
        var bmp = Bmp(40, 24, 1);

        var container = BmpContainer.Read(bmp[..30])!;

        Assert.Equal(new BmpHeader(40, 24, null), container.Fact<BmpFacts>()!.Headers.Single());
        Assert.True(container.Damaged);
        Assert.Equal(new BmpHeader(null, null, null), BmpContainer.Read(bmp[..16])!.Fact<BmpFacts>()!.Headers.Single());
    }

    [Fact]
    public void AnIconsEntriesAreReadUpToTheFirstCutShort()
    {
        var ico = Ico(new IconEntry(16, 16, 4), new IconEntry(0, 0, 32), new IconEntry(48, 48, 8));

        Assert.Equal(
            [new IcoEntry(16, 16, 4), new IcoEntry(256, 256, 32), new IcoEntry(48, 48, 8)],
            IcoContainer.Read(ico)!.Fact<IcoFacts>()!.Entries);

        Assert.Equal([new IcoEntry(16, 16, 4), new IcoEntry(256, 256, null)], IcoContainer.Read(ico[..(6 + 16 + 3)])!.Fact<IcoFacts>()!.Entries);
        Assert.Equal([new IcoEntry(16, 16, 4), new IcoEntry(null, null, null)], IcoContainer.Read(ico[..(6 + 16 + 1)])!.Fact<IcoFacts>()!.Entries);
        Assert.Null(IcoContainer.Read([0, 0, 2, 0, 1, 0]));
    }

    [Fact]
    public void ATgaIsAHeaderWhoseEveryFieldIsAllowed()
    {
        Assert.Equal(new TgaFacts(24), TgaContainer.ReadHeader(Tga()));
        Assert.Equal(new TgaFacts(8), TgaContainer.ReadHeader(Tga(dataType: 1, depth: 8, colormapType: 1)));
        Assert.Equal(new TgaFacts(16), TgaContainer.ReadHeader(Tga(dataType: 11, depth: 16, flags: 0x28)));

        byte[][] refused =
        [
            Tga(dataType: 4), Tga(dataType: 1, depth: 8), Tga(dataType: 2, colormapType: 1), Tga(dataType: 1, depth: 8, colormapType: 1, colormapDepth: 8),
            Tga(width: 0), Tga(height: -1), Tga(depth: 15), Tga(dataType: 3, depth: 24), Tga(depth: 8), Tga(flags: 0x40), Tga(flags: 0x03), Tga()[..17]
        ];

        Assert.All(refused, header => Assert.Null(TgaContainer.ReadHeader(header)));
    }

    [Fact]
    public void PhotoshopResourcesGiveTheirMetadataInOrder()
    {
        var container = new ContainerContents();
        container.AddPhotoshop([
            new PhotoshopResource(PhotoshopResources.Xmp, "", [1]),
            new PhotoshopResource(0x03ED, "", [2]),
            new PhotoshopResource(PhotoshopResources.ExifData3, "", [3]),
            new PhotoshopResource(PhotoshopResources.IccProfile, "", [4]),
            new PhotoshopResource(PhotoshopResources.Iptc, "", [5])
        ]);

        Assert.Equal([MetadataBlockKind.Xmp, MetadataBlockKind.Exif, MetadataBlockKind.Icc, MetadataBlockKind.Iptc], container.Blocks.Select(block => block.Kind));
        Assert.Equal([1, 3, 4, 5], container.Blocks.Select(block => block.Data[0]));
    }

    [Fact]
    public void DamagedFilesNeverThrow()
    {
        byte[][] files =
        [
            Bmp(124, 32, 3, Profile), BitmapArray(Bmp(64, 4, 3), Bmp(108, 24, magic: "CP")),
            Ico(new IconEntry(16, 16, 4), new IconEntry(0, 0, 32)), Tga(dataType: 9, depth: 8, colormapType: 1)
        ];
        var random = new Random(53);

        for (var variant = 0; variant < 800; variant++)
        {
            var source = files[variant % files.Length];
            var damaged = variant % 4 == 0 ? source[..random.Next(source.Length)] : (byte[])source.Clone();
            if (variant % 4 != 0)
                for (var i = 0; i < 1 + variant % 6; i++)
                    damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));

            _ = BmpContainer.Read(damaged)?.Fact<BmpFacts>()?.Headers.Count;
            _ = IcoContainer.Read(damaged)?.Fact<IcoFacts>()?.Entries.Count;
            _ = TgaContainer.Read(damaged);
        }
    }
}