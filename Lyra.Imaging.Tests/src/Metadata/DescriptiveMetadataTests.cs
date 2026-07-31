using Lyra.Imaging.Content;
using Lyra.Imaging.Metadata;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Metadata;

/// <summary>
/// Covers the descriptive layer - the parts a person writes rather than the camera - and the
/// precedence between the three places they can live: EXIF, XMP and IPTC.
/// </summary>
public class DescriptiveMetadataTests
{
    [Fact]
    public void Xmp_SuppliesTitleDescriptionKeywordsAndRating()
    {
        var path = ExifJpegBuilder.Write(xmp: new ExifJpegBuilder.XmpFields(
            Title: "Sunset over the bay",
            Description: "Shot from the north pier",
            Keywords: ["sunset", "sea", "pier"],
            Rating: 4));

        try
        {
            var rows = MetadataProcessor.ParseMetadata(path).ToKeyValuePairs();

            Assert.Contains(new ExifEntry("Title", "Sunset over the bay"), rows);
            Assert.Contains(new ExifEntry("Description", "Shot from the north pier"), rows);
            Assert.Contains(new ExifEntry("Keywords", "sunset, sea, pier"), rows);
            Assert.Contains(new ExifEntry("Rating", "4 / 5"), rows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Iptc_SuppliesTheSameFieldsWhenThereIsNoXmp()
    {
        var path = ExifJpegBuilder.Write(iptc: new ExifJpegBuilder.IptcFields(
            ObjectName: "Harbour",
            Caption: "Fishing boats at dawn",
            Keywords: ["harbour", "boats"],
            ByLine: "A. Photographer"));

        try
        {
            var rows = MetadataProcessor.ParseMetadata(path).ToKeyValuePairs();

            Assert.Contains(new ExifEntry("Title", "Harbour"), rows);
            Assert.Contains(new ExifEntry("Description", "Fishing boats at dawn"), rows);
            Assert.Contains(new ExifEntry("Keywords", "harbour, boats"), rows);
            Assert.Contains(new ExifEntry("Artist", "A. Photographer"), rows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Xmp_WinsOverIptcWhenBothArePresent()
    {
        // Writers keep XMP current and leave the IPTC block behind as a legacy copy.
        var path = ExifJpegBuilder.Write(
            xmp: new ExifJpegBuilder.XmpFields(Title: "Current title", Keywords: ["current"]),
            iptc: new ExifJpegBuilder.IptcFields(ObjectName: "Stale title", Keywords: ["stale"]));

        try
        {
            var rows = MetadataProcessor.ParseMetadata(path).ToKeyValuePairs();

            Assert.Contains(new ExifEntry("Title", "Current title"), rows);
            Assert.Contains(new ExifEntry("Keywords", "current"), rows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Artist_PrefersExifOverXmpAndIptc()
    {
        var path = ExifJpegBuilder.Write(
            artist: "From EXIF",
            xmp: new ExifJpegBuilder.XmpFields(Creator: "From XMP"),
            iptc: new ExifJpegBuilder.IptcFields(ByLine: "From IPTC"));

        try
        {
            Assert.Contains(new ExifEntry("Artist", "From EXIF"),
                MetadataProcessor.ParseMetadata(path).ToKeyValuePairs());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Artist_FallsBackToXmpWhenExifIsSilent()
    {
        var path = ExifJpegBuilder.Write(
            xmp: new ExifJpegBuilder.XmpFields(Creator: "From XMP"),
            iptc: new ExifJpegBuilder.IptcFields(ByLine: "From IPTC"));

        try
        {
            Assert.Contains(new ExifEntry("Artist", "From XMP"),
                MetadataProcessor.ParseMetadata(path).ToKeyValuePairs());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Keywords_AreSummarizedRatherThanRunningOffTheRow()
    {
        // Stock photography routinely tags dozens; the panel renders one unwrapped line per row.
        var keywords = Enumerable.Range(1, 20).Select(i => $"kw{i}").ToArray();
        var path = ExifJpegBuilder.Write(xmp: new ExifJpegBuilder.XmpFields(Keywords: keywords));

        try
        {
            var value = MetadataProcessor.ParseMetadata(path).ToKeyValuePairs()
                .First(entry => entry.Key == "Keywords").Value;

            Assert.StartsWith("kw1, kw2, kw3, kw4, kw5, kw6, kw7, kw8", value);
            Assert.EndsWith("(+12 more)", value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Description_IsCappedWithAnEllipsis()
    {
        var path = ExifJpegBuilder.Write(xmp: new ExifJpegBuilder.XmpFields(
            Description: new string('x', 400)));

        try
        {
            var value = MetadataProcessor.ParseMetadata(path).ToKeyValuePairs()
                .First(entry => entry.Key == "Description").Value;

            Assert.EndsWith("…", value);
            Assert.True(value.Length < 200, $"Expected a capped description, got {value.Length} characters.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0, null)] // unrated
    [InlineData(3, "3 / 5")]
    [InlineData(5, "5 / 5")]
    [InlineData(-1, "Rejected")]
    public void Rating_ShowsOnlyWhenItSaysSomething(int rating, string? expected)
    {
        var path = ExifJpegBuilder.Write(xmp: new ExifJpegBuilder.XmpFields(Rating: rating));

        try
        {
            var rows = MetadataProcessor.ParseMetadata(path).ToKeyValuePairs();

            if (expected is null)
                Assert.DoesNotContain(rows, entry => entry.Key == "Rating");
            else
                Assert.Contains(new ExifEntry("Rating", expected), rows);
        }
        finally
        {
            File.Delete(path);
        }
    }
}