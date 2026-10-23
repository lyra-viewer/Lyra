using System.IO.Compression;
using System.Text;
using Lyra.Metadata.Containers;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Icc;
using Lyra.Metadata.Tests.Support;
using Xunit;
using static Lyra.Metadata.Tests.Support.IsoFixture;

namespace Lyra.Metadata.Tests.Containers;

/// <summary>JPEG XL's plain and Brotli-compressed metadata boxes, and JPEG 2000's ICC profile.</summary>
public class JxlJp2ContainerTests
{
    private static readonly byte[] JxlSignature = [0, 0, 0, 0x0C, .. "JXL "u8, 0x0D, 0x0A, 0x87, 0x0A];
    private static readonly byte[] Jp2Signature = [0, 0, 0, 0x0C, .. "jP  "u8, 0x0D, 0x0A, 0x87, 0x0A];
    private static readonly byte[] Tiff = TiffFixture.Build([TiffFixture.Ascii(ExifTags.Software, "Pixelmator Pro")]);
    private static readonly byte[] Profile = IccFixture.Profile("RGB ", 2, IccFixture.Desc("eciRGB v2"));
    private const string Packet = "<x:xmpmeta xmlns:x='adobe:ns:meta/'/>";

    private static byte[] Brotli(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest))
            brotli.Write(data);

        return output.ToArray();
    }

    private static byte[] Brob(string type, byte[] data) => Box("brob", Ascii(type), Brotli(data));

    [Fact]
    public void CompressedBoxesAreReadLikePlainOnes()
    {
        var jxl = (byte[])[.. JxlSignature, .. Brob("Exif", [0, 0, 0, 0, .. Tiff]), .. Brob("xml ", Encoding.UTF8.GetBytes(Packet))];

        var container = JxlContainer.Read(jxl)!;

        Assert.Equal(Tiff, container.Blocks.Single(block => block.Kind == MetadataBlockKind.Exif).Data);
        Assert.Equal(Packet, Encoding.UTF8.GetString(container.Blocks.Single(block => block.Kind == MetadataBlockKind.Xmp).Data));
    }

    [Fact]
    public void TheFirstBoxOfAKindWinsWhetherCompressedOrNot()
    {
        var other = TiffFixture.Build([TiffFixture.Ascii(ExifTags.Software, "Second")]);
        var jxl = (byte[])[.. JxlSignature, .. Box("Exif", [0, 0, 0, 0, .. Tiff]), .. Brob("Exif", [0, 0, 0, 0, .. other])];

        Assert.Equal(Tiff, JxlContainer.Read(jxl)!.Blocks.Single().Data);
    }

    [Fact]
    public void ABrokenCompressedBoxIsPassedOver()
    {
        var jxl = (byte[])[.. JxlSignature, .. Box("brob", Ascii("Exif"), [1, 2, 3, 4, 5]), .. Brob("xml ", Encoding.UTF8.GetBytes(Packet))];

        var container = JxlContainer.Read(jxl)!;

        Assert.Equal([MetadataBlockKind.Xmp], container.Blocks.Select(block => block.Kind));
    }

    [Fact]
    public void AJp2sProfileComesFromItsHeadersColorBox()
    {
        var enumerated = Box("colr", [1, 0, 0], U32(16));
        var restricted = Box("colr", [2, 0, 0], Profile);
        var jp2 = (byte[])[.. Jp2Signature, .. Box("jp2h", Box("ihdr", new byte[14]), enumerated, restricted), .. Box("jp2c", [0xFF, 0x4F])];

        var container = Jp2Container.Read(jp2)!;

        Assert.Equal("eciRGB v2", IccProfile.Parse(container.Blocks.Single(block => block.Kind == MetadataBlockKind.Icc).Data)!.Description);
    }

    [Fact]
    public void AJp2WithOnlyAnEnumeratedColorSpaceHasNoProfile()
    {
        var jp2 = (byte[])[.. Jp2Signature, .. Box("jp2h", Box("colr", [1, 0, 0], U32(16)))];

        Assert.True(Jp2Container.Read(jp2)!.IsEmpty);
    }
}