using System.Text;
using Lyra.Imaging.Metadata;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Iptc;
using Lyra.Metadata.Photoshop;
using Xunit;
using static Lyra.Metadata.Tests.Support.TiffFixture;
using Icc = Lyra.Metadata.Tests.Support.IccFixture;
using Blocks = Lyra.Metadata.Tests.Support.BlockFixture;

namespace Lyra.Imaging.Tests.Metadata;

/// <summary>A TIFF's blocks reach the panel in the order they always have: the first of a kind wins, even unreadable.</summary>
public class BlockOrderTests
{
    [Fact]
    public void ATiffsBlocksReachThePanelInTheOrderTheyAlwaysHave()
    {
        // XMP and IPTC from IFD0 before Photoshop's copies; ICC from Photoshop's copy before IFD0's.
        byte[] photoshop =
        [
            .. Blocks.Resource(PhotoshopResources.Xmp, Encoding.UTF8.GetBytes(Packet("""<dc:title><rdf:Alt><rdf:li xml:lang="x-default">From Photoshop</rdf:li></rdf:Alt></dc:title>"""))),
            .. Blocks.Resource(PhotoshopResources.Iptc, Blocks.Text(IptcRecords.ByLine, "Photoshop byline")),
            .. Blocks.Resource(PhotoshopResources.IccProfile, Icc.Profile("RGB ", 4, Icc.Mluc(["enUS", "Display P3"])))
        ];

        var file = Build(
        [
            Undefined(ExifTags.Xmp, Encoding.UTF8.GetBytes(Packet("""<dc:title><rdf:Alt><rdf:li xml:lang="x-default">From IFD0</rdf:li></rdf:Alt></dc:title>"""))),
            Undefined(ExifTags.Iptc, Blocks.Text(IptcRecords.ByLine, "IFD0 byline")),
            Undefined(ExifTags.Photoshop, photoshop),
            Undefined(ExifTags.IccProfile, Icc.Profile("RGB ", 2, Icc.Desc("sRGB IEC61966-2.1")))
        ]);

        using var stream = new MemoryStream(file, writable: false);
        var info = MetadataProcessor.ParseMetadata(stream, "synthetic.tif");

        Assert.Equal("From IFD0", info.Title);
        Assert.Equal("IFD0 byline", info.Artist);
        Assert.Equal("Display P3", info.IccProfile);
    }

    [Fact]
    public void AnUnreadableFirstBlockStillHidesTheOneAfterIt()
    {
        byte[] photoshop = [.. Blocks.Resource(PhotoshopResources.Xmp, Encoding.UTF8.GetBytes(Packet("""<dc:title><rdf:Alt><rdf:li xml:lang="x-default">From Photoshop</rdf:li></rdf:Alt></dc:title>""")))];
        var file = Build([Undefined(ExifTags.Xmp, "<x:xmpmeta><rdf:RDF"u8.ToArray()), Undefined(ExifTags.Photoshop, photoshop)]);

        using var stream = new MemoryStream(file, writable: false);

        Assert.Equal(string.Empty, MetadataProcessor.ParseMetadata(stream, "synthetic.tif").Title);
    }

    private static string Packet(string body) => $"""<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"><rdf:Description xmlns:dc="http://purl.org/dc/elements/1.1/">{body}</rdf:Description></rdf:RDF>""";
}