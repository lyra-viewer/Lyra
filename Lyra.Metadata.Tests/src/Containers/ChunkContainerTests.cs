using System.Text;
using Lyra.Metadata.Containers;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Tests.Support;
using Xunit;
using static Lyra.Metadata.Tests.Support.ChunkFixture;

namespace Lyra.Metadata.Tests.Containers;

/// <summary>PNG, WebP and GIF: blocks in file order, the once-only PNG chunks taken first, damage kept short.</summary>
public class ChunkContainerTests
{
    private static readonly byte[] Tiff = TiffFixture.Build([TiffFixture.Ascii(ExifTags.Make, "A camera make")]);
    private static readonly byte[] Profile = IccFixture.Profile("RGB ", 2, IccFixture.Desc("A profile"));
    private const string Packet = "<x:xmpmeta xmlns:x='adobe:ns:meta/'/>";

    private static MemoryStream Streamed(byte[] data) => new(data, writable: false);

    private static string Text(MetadataBlock block) => Encoding.UTF8.GetString(block.Data);

    [Fact]
    public void APngsBlocksComeInFileOrderWithItsHeaderFacts()
    {
        var png = Png(Ihdr(16, 6), ITxt("XML:com.adobe.xmp", Packet), Chunk("eXIf", Tiff), Iccp("My Profile", Profile));

        var container = PngContainer.Read(Streamed(png))!;

        Assert.Equal([MetadataBlockKind.Xmp, MetadataBlockKind.Exif, MetadataBlockKind.Icc], container.Blocks.Select(block => block.Kind));
        Assert.Equal(Packet, Text(container.Blocks[0]));
        Assert.Equal(Profile, container.Blocks[2].Data);
        Assert.Equal(new PngFacts(16, 6, 0, "My Profile"), container.Fact<PngFacts>());
    }

    [Fact]
    public void PngXmpIsReadFromITxtAndZTxtButNotTExt()
    {
        var png = Png(Ihdr(), TExt("XML:com.adobe.xmp", "plain"), ITxt("XML:com.adobe.xmp", "zipped", compressed: true), ZTxt("XML:com.adobe.xmp", "ztxt"), ITxt("Comment", "not xmp"));

        var container = PngContainer.Read(Streamed(png))!;

        Assert.Equal(["zipped", "ztxt"], container.Blocks.Select(Text));
    }

    [Fact]
    public void ImageMagicksRawProfilesAreReadFromZTxtAndITxtButNotTExt()
    {
        var png = Png(Ihdr(),
            ZTxt("Raw profile type exif", RawProfile("exif", [.. "Exif\0\0"u8, .. Tiff])),
            ITxt("Raw profile type iptc", RawProfile("iptc", [0x1C, 2, 5, 0, 1, (byte)'T'])),
            TExt("Raw profile type xmp", RawProfile("xmp", "<ignored/>"u8.ToArray())),
            ZTxt("Raw profile type icc", RawProfile("icc", Profile)),
            ZTxt("Raw profile type 8bim", RawProfile("8bim", [1, 2, 3])));

        var container = PngContainer.Read(Streamed(png))!;

        Assert.Equal([MetadataBlockKind.Exif, MetadataBlockKind.Iptc, MetadataBlockKind.Icc], container.Blocks.Select(block => block.Kind));
        Assert.Equal(Tiff, container.Blocks[0].Data);
        Assert.Equal(Profile, container.Blocks[2].Data);
    }

    [Fact]
    public void ARawProfileTakesOnlyAsManyBytesAsItsLengthSays()
    {
        var png = Png(Ihdr(), ZTxt("Raw profile type APP1", "\nAPP1\n       4\n0102030405060708\n"));

        Assert.Equal([1, 2, 3, 4], PngContainer.Read(Streamed(png))!.Blocks.Single().Data);
    }

    [Fact]
    public void ThePngChunksAllowedOnceAreTakenTheFirstTime()
    {
        var png = Png(Ihdr(8, 2), Ihdr(16, 0), Chunk("eXIf", Tiff), Chunk("eXIf", [1, 2]), Iccp("First", Profile), Iccp("Second", Profile));

        var container = PngContainer.Read(Streamed(png))!;

        Assert.Equal(new PngFacts(8, 2, 0, "First"), container.Fact<PngFacts>());
        Assert.Equal(Tiff, container.Blocks.Single(block => block.Kind == MetadataBlockKind.Exif).Data);
        Assert.Single(container.Blocks, block => block.Kind == MetadataBlockKind.Icc);
    }

    [Fact]
    public void APngCutShortKeepsItsWholeChunksAndSaysSo()
    {
        var png = Png(Ihdr(), Chunk("eXIf", Tiff), ITxt("XML:com.adobe.xmp", Packet));

        var container = PngContainer.Read(Streamed(png[..^20]))!;

        Assert.True(container.Damaged);
        Assert.Equal([MetadataBlockKind.Exif], container.Blocks.Select(block => block.Kind));
    }

    [Fact]
    public void APngIsOneWhoseFirstChunkIsIhdr()
    {
        Assert.False(PngContainer.Recognizes(Png(Chunk("eXIf", Tiff), Ihdr())));
        Assert.Null(PngContainer.Read(Streamed([0x89, 0x50, 0x4E, 0x47])));
    }

    [Fact]
    public void AWebpsBlocksComeInFileOrderWithOrWithoutTheExifPreamble()
    {
        var webp = Webp(RiffChunk("XMP ", Encoding.UTF8.GetBytes(Packet)), Vp8L, RiffChunk("EXIF", [.. "Exif\0\0"u8, .. Tiff]), RiffChunk("ICCP", Profile), RiffChunk("EXIF", Tiff));

        var container = WebpContainer.Read(Streamed(webp))!;

        Assert.Equal([MetadataBlockKind.Xmp, MetadataBlockKind.Exif, MetadataBlockKind.Icc, MetadataBlockKind.Exif], container.Blocks.Select(block => block.Kind));
        Assert.Equal(Tiff, container.Blocks[1].Data);
        Assert.Equal(Tiff, container.Blocks[3].Data);
    }

    [Fact]
    public void AWebpCutShortKeepsWhatCameBefore()
    {
        var webp = Webp(Vp8L, RiffChunk("EXIF", Tiff), RiffChunk("XMP ", Encoding.UTF8.GetBytes(Packet)));

        var container = WebpContainer.Read(Streamed(webp[..^10]))!;

        Assert.True(container.Damaged);
        Assert.Equal([MetadataBlockKind.Exif], container.Blocks.Select(block => block.Kind));
    }

    [Fact]
    public void AGifsXmpAndIccAreFoundWhereverTheyAre()
    {
        var gif = Gif(GifComment("hello"), GifXmp(Packet), GifImage, GifIcc(Profile), GifXmp("<second/>"));

        var container = GifContainer.Read(gif)!;

        Assert.Equal([MetadataBlockKind.Xmp, MetadataBlockKind.Icc, MetadataBlockKind.Xmp], container.Blocks.Select(block => block.Kind));
        Assert.Equal(Packet, Text(container.Blocks[0]));
        Assert.Equal(Profile, container.Blocks[1].Data);
        Assert.False(container.Damaged);
    }

    [Fact]
    public void AGifCutShortKeepsWhatCameBefore()
    {
        var gif = Gif(GifXmp(Packet), GifImage, GifIcc(Profile));

        var container = GifContainer.Read(gif[..^60])!;

        Assert.True(container.Damaged);
        Assert.Equal([MetadataBlockKind.Xmp], container.Blocks.Select(block => block.Kind));
    }

    [Fact]
    public void DamagedFilesNeverThrow()
    {
        byte[][] files =
        [
            Png(Ihdr(), Iccp("P", Profile), Chunk("eXIf", Tiff), ITxt("XML:com.adobe.xmp", Packet, compressed: true)),
            Webp(Vp8L, RiffChunk("EXIF", Tiff), RiffChunk("XMP ", Encoding.UTF8.GetBytes(Packet)), RiffChunk("ICCP", Profile)),
            Gif(GifXmp(Packet), GifImage, GifIcc(Profile), GifComment("c"))
        ];
        var random = new Random(31);

        for (var variant = 0; variant < 600; variant++)
        {
            var source = files[variant % 3];
            var damaged = variant % 4 == 0 ? source[..random.Next(source.Length)] : (byte[])source.Clone();
            if (variant % 4 != 0)
                for (var i = 0; i < 1 + variant % 6; i++)
                    damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));

            _ = PngContainer.Read(Streamed(damaged))?.Blocks.Count;
            _ = WebpContainer.Read(Streamed(damaged))?.Blocks.Count;
            _ = GifContainer.Read(damaged)?.Blocks.Count;
        }
    }
}