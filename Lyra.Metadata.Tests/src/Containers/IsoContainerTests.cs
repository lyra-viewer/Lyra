using System.Text;
using Lyra.Metadata.Containers;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Tests.Support;
using Lyra.Metadata.Tiff;
using Xunit;
using static Lyra.Metadata.Tests.Support.IsoFixture;

namespace Lyra.Metadata.Tests.Containers;

public class IsoContainerTests
{
    private static readonly byte[] Tiff = TiffFixture.Build([TiffFixture.Ascii(ExifTags.Make, "A camera make")]);
    private static readonly byte[] BigEndianTiff = TiffFixture.Build(little: false, big: false, [TiffFixture.Ascii(ExifTags.Make, "Big")]);
    private static readonly byte[] Profile = IccFixture.Profile("RGB ", 2, IccFixture.Desc("A profile"));
    private static readonly byte[] OtherProfile = IccFixture.Profile("GRAY", 4, IccFixture.Desc("Another"));
    private const string Packet = "<x:xmpmeta xmlns:x='adobe:ns:meta/'/>";

    private static ContainerContents Read(byte[] file) => HeifContainer.Read(new MemoryStream(file))!;

    private static Item Image(uint id, string type = "hvc1") => new(id, type, [0xAA, 0xBB]);

    private static Item Xmp(uint id, string packet = Packet) => new(id, "mime", Encoding.UTF8.GetBytes(packet), ContentType: "application/rdf+xml");

    [Fact]
    public void AHeifsProfileComesFirstThenItsItemsInFileOrder()
    {
        var heif = new Heif
        {
            Items = [Image(1), new Item(2, "Exif", ExifItem(Tiff)), Xmp(3)],
            References = [new Reference("cdsc", 2, 1), new Reference("cdsc", 3, 1)],
            Properties = [Ispe(8, 8), Colr("prof", Profile), Hvcc(10), Irot(1)],
            Associations = [new Association(1, 1, 2, 3, 4)],
            DataOrder = [3, 1, 2]
        };

        var container = Read(heif.Build());

        Assert.Equal([MetadataBlockKind.Icc, MetadataBlockKind.Xmp, MetadataBlockKind.Exif], container.Blocks.Select(block => block.Kind));
        Assert.Equal(Profile, container.Blocks[0].Data);
        Assert.Equal(Packet, Encoding.UTF8.GetString(container.Blocks[1].Data));
        Assert.Equal(Tiff, container.Blocks[2].Data);
        Assert.Equal(new HeifFacts(10, 90), container.Fact<HeifFacts>());
        Assert.False(container.Damaged);
    }

    [Fact]
    public void AnExifItemsTiffIsFoundPastItsOffsetOrStraightAway()
    {
        byte[][] items = [ExifItem(Tiff), ExifItem(Tiff, preamble: false), BigEndianTiff, Tiff];
        byte[][] expected = [Tiff, Tiff, BigEndianTiff, Tiff];

        for (var i = 0; i < items.Length; i++)
        {
            var heif = new Heif { Items = [Image(1), new Item(2, "Exif", items[i])], References = [new Reference("cdsc", 2, 1)] };
            Assert.Equal(expected[i], Read(heif.Build()).Blocks.Single().Data);
        }
    }

    [Fact]
    public void OnlyReferencedItemsStoredInTheFileAreRead()
    {
        var heif = new Heif
        {
            Items = [Image(1), new Item(2, "Exif", ExifItem(Tiff)), new Item(3, "Exif", ExifItem(Tiff), Construction: 1), Xmp(4)],
            References = [new Reference("cdsc", 3, 1)]
        };

        Assert.Empty(Read(heif.Build()).Blocks);
    }

    [Fact]
    public void AGridTakesItsTilesProfileAndBitDepthOnce()
    {
        var heif = new Heif
        {
            Items = [Image(1, "grid"), Image(10), Image(11)],
            References = [new Reference("dimg", 1, 10, 11)],
            Properties = [Ispe(16, 8), Irot(2), Hvcc(8), Colr("prof", Profile)],
            Associations = [new Association(1, 1, 2), new Association(10, 3, 4), new Association(11, 3, 4)]
        };

        var container = Read(heif.Build());

        Assert.Equal(Profile, container.Blocks.Single().Data);
        Assert.Equal(new HeifFacts(8, 180), container.Fact<HeifFacts>());
    }

    [Fact]
    public void EveryColorBoxButNclxIsOfferedAsAProfile()
    {
        var heif = new Heif
        {
            Items = [Image(1)],
            Properties = [Nclx(), Colr("rICC", Profile), Colr("what", Profile)],
            Associations = [new Association(1, 1, 2, 3)]
        };

        var container = Read(heif.Build());

        Assert.Equal([Profile, []], container.Blocks.Select(block => block.Data));
        Assert.Null(container.Fact<HeifFacts>());
    }

    [Fact]
    public void ADescribedItemsPropertiesFollowThePrimarysAndNeverTurnIt()
    {
        var heif = new Heif
        {
            Items = [Image(1), Image(5)],
            References = [new Reference("thmb", 5, 1)],
            Properties = [Colr("prof", Profile), Colr("prof", OtherProfile), Hvcc(12), Irot(3)],
            Associations = [new Association(1, 1), new Association(5, 2, 3, 4)]
        };

        var container = Read(heif.Build());

        Assert.Equal([Profile, OtherProfile], container.Blocks.Select(block => block.Data));
        Assert.Equal(new HeifFacts(12, null), container.Fact<HeifFacts>());
    }

    [Fact]
    public void WithinAnItemTheLastRotationAndBitDepthStand()
    {
        var heif = new Heif { Items = [Image(1)], Properties = [Irot(1), Hvcc(8), Irot(3), Hvcc(10)], Associations = [new Association(1, 1, 2, 3, 4)] };

        Assert.Equal(new HeifFacts(10, 270), Read(heif.Build()).Fact<HeifFacts>());
    }

    [Fact]
    public void BrandsDecideBetweenHeifAndQuickTime()
    {
        foreach (var brand in new[] { "heic", "mif1", "msf1", "heix", "hevc", "hevx", "avif" })
        {
            Assert.True(HeifContainer.Recognizes(Ftyp(brand)));
            Assert.False(QuickTimeContainer.Recognizes(Ftyp(brand)));
        }

        foreach (var brand in new[] { "crx ", "avis", "isom", "qt  " })
        {
            Assert.False(HeifContainer.Recognizes(Ftyp(brand)));
            Assert.True(QuickTimeContainer.Recognizes(Ftyp(brand)));
        }

        Assert.Null(HeifContainer.Read(new MemoryStream(Cr3(Box("moov")))));
        Assert.Null(QuickTimeContainer.Read(new MemoryStream(new Heif().Build())));
    }

    [Fact]
    public void ACr3sBlocksAreItsCanonTiffsAndXmpInFileOrder()
    {
        var exif = TiffFixture.Build([TiffFixture.Short(ExifTags.IsoEquivalent, 400)]);
        var gps = TiffFixture.Build([TiffFixture.Ascii(ExifTags.GpsLatitudeRef, "N")]);
        var moov = Box("moov",
            Canon(Box("CNCV", Ascii("CanonCR3")), Box("CMT1", Tiff), Box("CMT2", exif), Box("CMT3", Tiff), Box("CMT4", gps)),
            Box("udta", Box("XMP_", Encoding.UTF8.GetBytes("<udta/>"), [0, 1, 2])));

        var container = QuickTimeContainer.Read(new MemoryStream(Cr3(moov, XmpBox(Packet, padding: 64))))!;

        Assert.Equal(
            [
                new MetadataBlock(MetadataBlockKind.Exif, Tiff, TiffDirectoryKind.Image),
                new MetadataBlock(MetadataBlockKind.Exif, exif, TiffDirectoryKind.Exif),
                new MetadataBlock(MetadataBlockKind.Exif, gps, TiffDirectoryKind.Gps)
            ],
            container.Blocks.Take(3), MetadataBlockEquality.Instance);
        Assert.Equal(["<udta/>", Packet], container.Blocks.Skip(3).Select(block => Encoding.UTF8.GetString(block.Data)));
        Assert.False(container.Damaged);
    }

    [Fact]
    public void ATiffWhoseFirstDirectoryIsExifFollowsOnlyExifPointers()
    {
        var cmt2 = TiffFixture.Build([
            TiffFixture.Short(ExifTags.IsoEquivalent, 400),
            TiffFixture.Pointer(TiffDirectoryReader.TagInteropIfd, TiffFixture.Ascii(1, "R98")),
            TiffFixture.Pointer(TiffDirectoryReader.TagGpsIfd, TiffFixture.Ascii(ExifTags.GpsLatitudeRef, "N"))
        ]);

        var exif = ExifMetadata.Read(new MemoryStream(cmt2), TiffDirectoryKind.Exif)!;

        Assert.False(exif.Has(TiffDirectoryKind.Image));
        Assert.False(exif.Has(TiffDirectoryKind.Gps));
        Assert.Equal(400, exif.TryGetInt32(TiffDirectoryKind.Exif, ExifTags.IsoEquivalent, out var iso) ? iso : 0);
        Assert.Equal("R98", exif.Get(TiffDirectoryKind.Interop, 1)?.Text);
    }

    [Fact]
    public void DamagedFilesNeverThrow()
    {
        var heif = new Heif
        {
            Items = [Image(1, "grid"), new Item(2, "Exif", ExifItem(Tiff)), Xmp(3), Image(5), Image(10)],
            References = [new Reference("cdsc", 2, 1), new Reference("cdsc", 3, 1), new Reference("thmb", 5, 1), new Reference("dimg", 1, 10)],
            Properties = [Colr("prof", Profile), Hvcc(10), Irot(1), Nclx()],
            Associations = [new Association(1, 1, 3), new Association(5, 2, 4), new Association(10, 2)]
        }.Build();

        var cr3 = Cr3(Box("moov", Canon(Box("CMT1", Tiff), Box("CMT2", Tiff), Box("CMT4", Tiff)), Box("udta", Box("XMP_", Ascii(Packet)))), XmpBox(Packet, 8));

        byte[][] files = [heif, cr3];
        var random = new Random(47);

        for (var variant = 0; variant < 600; variant++)
        {
            var source = files[variant % 2];
            var damaged = variant % 4 == 0 ? source[..random.Next(source.Length)] : (byte[])source.Clone();
            if (variant % 4 != 0)
                for (var i = 0; i < 1 + variant % 6; i++)
                    damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));

            foreach (var block in HeifContainer.Read(new MemoryStream(damaged))?.Blocks ?? [])
                _ = ExifMetadata.Read(new MemoryStream(block.Data), block.FirstDirectory);

            foreach (var block in QuickTimeContainer.Read(new MemoryStream(damaged))?.Blocks ?? [])
                _ = ExifMetadata.Read(new MemoryStream(block.Data), block.FirstDirectory);
        }
    }

    private sealed class MetadataBlockEquality : IEqualityComparer<MetadataBlock>
    {
        public static readonly MetadataBlockEquality Instance = new();

        public bool Equals(MetadataBlock? x, MetadataBlock? y) =>
            x is not null && y is not null && x.Kind == y.Kind && x.FirstDirectory == y.FirstDirectory && x.Data.SequenceEqual(y.Data);

        public int GetHashCode(MetadataBlock block) => HashCode.Combine(block.Kind, block.FirstDirectory);
    }
}