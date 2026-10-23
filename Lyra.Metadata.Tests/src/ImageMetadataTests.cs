using System.Text;
using Lyra.Metadata.Containers;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Photoshop;
using Lyra.Metadata.Tests.Support;
using Lyra.Metadata.Tiff;
using Lyra.Metadata.Xmp;
using Xunit;
using static Lyra.Metadata.Tests.Support.TiffFixture;

namespace Lyra.Metadata.Tests;

/// <summary>Which of a file's blocks are the ones taken: the first of each kind offered, in the order a reader meets them.</summary>
public class ImageMetadataTests
{
    private static string Packet(string title) =>
        $"<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='{XmpNamespaces.Rdf}'><rdf:Description xmlns:dc='{XmpNamespaces.Dc}' dc:format='{title}'/></rdf:RDF></x:xmpmeta>";

    private static readonly byte[] SrgbProfile = IccFixture.Profile("RGB ", 2, IccFixture.Desc("First"));
    private static readonly byte[] GrayProfile = IccFixture.Profile("GRAY", 2, IccFixture.Desc("Second"));

    private static ImageMetadata Read(byte[] file) => ImageMetadata.Read(new MemoryStream(file))!;

    [Fact]
    public void TheFirstBlockOfEachKindIsTaken()
    {
        var jpeg = JpegFixture.Build([JpegFixture.Xmp(Packet("first")), JpegFixture.Xmp(Packet("second")), .. JpegFixture.Icc(SrgbProfile)]);

        var metadata = Read(jpeg);

        Assert.Equal("first", metadata.Xmp!.First(XmpNamespaces.Dc, "format"));
        Assert.Equal("First", metadata.Icc!.Description);
        Assert.False(metadata.IsEmpty);
    }

    [Fact]
    public void AnUnreadableFirstBlockStillComesFirst()
    {
        var jpeg = JpegFixture.Build(JpegFixture.Xmp("<not xml"), JpegFixture.Xmp(Packet("second")));

        var metadata = Read(jpeg);

        Assert.Null(metadata.Xmp);
        Assert.False(metadata.IsEmpty);
    }

    [Fact]
    public void IfdZeroOffersItsBlocksInTagOrderWithPhotoshopsIccFirst()
    {
        var resources = (byte[])[.. BlockFixture.Resource(PhotoshopResources.Xmp, Encoding.UTF8.GetBytes(Packet("photoshop"))), .. BlockFixture.Resource(PhotoshopResources.IccProfile, GrayProfile)];
        var tiff = Build([Undefined(ExifTags.Xmp, Encoding.UTF8.GetBytes(Packet("ifd0"))), Undefined(ExifTags.Photoshop, resources), Undefined(ExifTags.IccProfile, SrgbProfile)]);

        var metadata = Read(tiff);

        Assert.Equal("ifd0", metadata.Xmp!.First(XmpNamespaces.Dc, "format"));
        Assert.Equal("Second", metadata.Icc!.Description);
    }

    [Fact]
    public void ExifIsTakenDirectoryByDirectoryFromTheFirstBlockThatHasIt()
    {
        var first = Build([Ascii(ExifTags.Make, "First")]);
        var second = Build([Ascii(ExifTags.Make, "Second"), Pointer(TiffDirectoryReader.TagGpsIfd, Ascii(ExifTags.GpsLatitudeRef, "N"))]);

        var exif = Read(JpegFixture.Build(JpegFixture.Exif(first), JpegFixture.Exif(second))).Exif!;

        Assert.Equal("First", exif.Get(TiffDirectoryKind.Image, ExifTags.Make)?.Text);
        Assert.Equal("N", exif.Get(TiffDirectoryKind.Gps, ExifTags.GpsLatitudeRef)?.Text);
    }

    [Fact]
    public void UnreadableUnknownAndEmptyAreTold()
    {
        Assert.True(Read([.. "II*\0"u8, 0xFF, 0xFF, 0, 0]).Unreadable);
        Assert.True(Read([0xFF, 0xD8, 0xFF, 0xE1, 0x7F]).Unreadable);
        Assert.Null(ImageMetadata.Read(new MemoryStream("plain text, no image"u8.ToArray())));

        var empty = ImageMetadata.From(ContainerContents.FromPhotoshop([new PhotoshopResource(0x03ED, "", [1, 2])]));
        Assert.True(empty.IsEmpty);
        Assert.False(empty.Unreadable);
    }

    [Fact]
    public void AStreamIsLeftWhereItWas()
    {
        using var stream = new MemoryStream(Build([Ascii(ExifTags.Make, "A make")]));
        stream.Position = 3;

        Assert.NotNull(ImageMetadata.Read(stream)!.Exif);
        Assert.Equal(3, stream.Position);
    }
}