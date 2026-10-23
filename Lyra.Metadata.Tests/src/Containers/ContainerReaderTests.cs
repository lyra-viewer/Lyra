using Lyra.Metadata.Containers;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Tests.Support;
using Xunit;

namespace Lyra.Metadata.Tests.Containers;

public class ContainerReaderTests
{
    private static readonly byte[] Tiff = TiffFixture.Build([TiffFixture.Ascii(ExifTags.Make, "A camera make")]);

    public static TheoryData<string> Formats => ["jpeg", "png", "webp", "heif", "cr3", "mrw", "jxl"];

    private static byte[] File(string format) => format switch
    {
        "jpeg" => JpegFixture.Build(JpegFixture.Exif(Tiff)),
        "png" => ChunkFixture.Png(ChunkFixture.Ihdr(), ChunkFixture.Chunk("eXIf", Tiff)),
        "webp" => ChunkFixture.Webp(ChunkFixture.Vp8L, ChunkFixture.RiffChunk("EXIF", Tiff)),
        "heif" => new IsoFixture.Heif
        {
            Items = [new IsoFixture.Item(1, "hvc1", [1]), new IsoFixture.Item(2, "Exif", IsoFixture.ExifItem(Tiff))],
            References = [new IsoFixture.Reference("cdsc", 2, 1)]
        }.Build(),
        "cr3" => IsoFixture.Cr3(IsoFixture.Box("moov", IsoFixture.Canon(IsoFixture.Box("CMT1", Tiff)))),
        "mrw" => MrwFixture.Build(Tiff),
        _ => [0, 0, 0, 0x0C, .. "JXL "u8, 0x0D, 0x0A, 0x87, 0x0A, .. IsoFixture.Box("Exif", [0, 0, 0, 0, .. Tiff])]
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public void EachFormatGivesItsExif(string format)
    {
        var container = ContainerReader.Read(new MemoryStream(File(format)))!;

        Assert.Equal(Tiff, container.Blocks.Single(block => block.Kind == MetadataBlockKind.Exif).Data);
    }

    [Fact]
    public void FormatsWithOnlyHeaderFactsAreFoundToo()
    {
        Assert.IsType<BmpFacts>(ContainerReader.Read(new MemoryStream(BitmapFixture.Bmp(40, 24)))!.Facts.Single());
        Assert.IsType<IcoFacts>(ContainerReader.Read(new MemoryStream(BitmapFixture.Ico(new BitmapFixture.IconEntry(16, 16, 32))))!.Facts.Single());
        Assert.Equal(new TgaFacts(24), ContainerReader.Read(new MemoryStream(BitmapFixture.Tga()))!.Fact<TgaFacts>());
    }

    [Fact]
    public void AnythingElseIsNoContainerAndThePositionStays()
    {
        using var stream = new MemoryStream("not an image at all, not even close"u8.ToArray());
        stream.Position = 5;

        Assert.Null(ContainerReader.Read(stream));
        Assert.Equal(5, stream.Position);
    }

    [Fact]
    public void AFileIsReadFurtherWhileItsStructureRunsOn()
    {
        // Past the first read of a file that is not already in memory.
        byte[][] padding = [.. Enumerable.Range(0, 6).Select(_ => JpegFixture.Segment(0xE5, new byte[60_000]))];
        var jpeg = JpegFixture.Build([.. padding, JpegFixture.Exif(Tiff)]);
        Assert.True(jpeg.Length > 256 * 1024);

        using var stream = new BufferedStream(new MemoryStream(jpeg));
        var container = ContainerReader.Read(stream)!;

        Assert.False(container.Damaged);
        Assert.Equal(Tiff, container.Blocks.Single().Data);
    }
}