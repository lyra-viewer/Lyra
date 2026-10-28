using Lyra.Imaging.Content.Exif;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Metadata;
using Lyra.Imaging.Tests.Support;
using Lyra.Metadata.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Metadata;

/// <summary>Camera-raw metadata: MRW's TTW block, LibRaw's fallback facts, and thumbnail rows kept off the panel.</summary>
public class RawMetadataTests
{
    private static readonly RawCapture NoCapture = new("", "", "", 0, 0, 0, 0, null);

    private static readonly RawCapture Capture = new("Canon", "PowerShot G2", "", 100, 1 / 501f, 6.3f, 7.5f, new DateTime(2002, 9, 8, 13, 32, 38));

    [Fact]
    public void AnMrw_IsReadFromItsTtwBlock()
    {
        using var stream = new MemoryStream(MrwFixture.Build(ExifJpegBuilder.BuildTiff(dateTimeOriginal: "2009:11:29 12:45:48", compression: 6)));

        var exif = MetadataProcessor.ParseRawMetadata(stream, "test.mrw", NoCapture);

        Assert.True(exif.IsValid());
        Assert.Equal("2009-11-29 12:45:48", exif.Taken);
        Assert.Equal("sRGB", exif.ColorSpace);
        Assert.Equal(string.Empty, exif.Compression);
    }

    [Fact]
    public void WithoutReadableExif_LibRawsFactsAreShown()
    {
        using var stream = new MemoryStream("not a raw file at all"u8.ToArray());

        var exif = MetadataProcessor.ParseRawMetadata(stream, "test.crw", Capture);

        Assert.True(exif.IsValid());
        Assert.Equal("Canon", exif.Make);
        Assert.Equal("PowerShot G2", exif.Model);
        Assert.Equal("100", exif.Iso);
        Assert.Equal("1/500 sec", exif.ExposureTime);
        Assert.Equal("f/6.3", exif.FNumber);
        Assert.Equal("7.5 mm", exif.FocalLength);
        Assert.Equal("2002-09-08 13:32:38", exif.Taken);
    }

    [Fact]
    public void WithNeitherExifNorLibRawFacts_TheReadFails()
    {
        using var stream = new MemoryStream("not a raw file at all"u8.ToArray());

        Assert.Equal(ExifStatus.Failed, MetadataProcessor.ParseRawMetadata(stream, "test.crw", NoCapture).Status);
    }

    [Fact]
    public void ExifValues_AreNotOverriddenByLibRaw()
    {
        using var stream = new MemoryStream(ExifJpegBuilder.BuildTiff(dateTimeOriginal: "2024:09:26 18:32:17", compression: 7));

        var exif = MetadataProcessor.ParseRawMetadata(stream, "test.dng", Capture);

        Assert.Equal("2024-09-26 18:32:17", exif.Taken);
        Assert.Equal("Canon", exif.Make);
        Assert.Equal(string.Empty, exif.Compression);
    }

    [Theory]
    [InlineData(1 / 501f, "1/500 sec")]
    [InlineData(1 / 1244f, "1/1250 sec")]
    [InlineData(1 / 4f, "1/4 sec")]
    [InlineData(1 / 90f, "1/90 sec")]
    [InlineData(0.3f, "0.3 sec")]
    [InlineData(2f, "2 sec")]
    [InlineData(0f, "")]
    [InlineData(float.NaN, "")]
    public void ShutterSpeeds_ReadLikeTheCameras(float seconds, string expected)
    {
        Assert.Equal(expected, RawMetadata.DescribeShutter(seconds));
    }

    [Theory]
    [InlineData("0.22.2-Release", "0.22.2")]
    [InlineData("0.22.2", "0.22.2")]
    [InlineData("", "")]
    public void LibRawsVersion_DropsTheBuildSuffix(string version, string expected)
    {
        Assert.Equal(expected, RawNative.ShortVersion(version));
    }
}
