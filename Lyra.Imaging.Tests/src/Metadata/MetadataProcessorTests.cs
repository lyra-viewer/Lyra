using Lyra.Imaging.Content;
using Lyra.Imaging.Metadata;
using Lyra.Imaging.Tests.Support;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Tiff;
using SkiaSharp;
using Xunit;
using static Lyra.Metadata.Tests.Support.TiffFixture;

namespace Lyra.Imaging.Tests.Metadata;

/// <summary>
/// Covers the normalization layer between Lyra.Metadata and the panel: placeholder values
/// must not occupy rows or outrank real ones, and raw library formatting must not reach the UI.
/// </summary>
public class MetadataProcessorTests
{
    [Theory]
    [InlineData("Undefined")]
    [InlineData("undefined")]
    [InlineData("Unknown")]
    [InlineData("Unknown (8)")]
    [InlineData("Reserved")]
    [InlineData("N/A")]
    [InlineData("-")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Clean_TreatsPlaceholdersAsAbsent(string? value)
    {
        Assert.Equal(string.Empty, MetadataValues.Clean(value));
    }

    [Theory]
    [InlineData("sRGB", "sRGB")]
    [InlineData("RGB ", "RGB")]                           // ICC pads codes to four characters
    [InlineData("Unknown artist", "Unknown artist")]      // only the placeholder word alone counts
    [InlineData("Undefined Behaviour Ltd", "Undefined Behaviour Ltd")]
    public void Clean_KeepsRealValues(string value, string expected)
    {
        Assert.Equal(expected, MetadataValues.Clean(value));
    }

    [Fact]
    public void ParseMetadata_FormatsTheCaptureTimeInsteadOfPassingTheWireFormatThrough()
    {
        var path = ExifJpegBuilder.Write(dateTimeOriginal: "2024:09:26 18:32:17");

        try
        {
            var info = MetadataProcessor.ParseMetadata(path);

            Assert.Contains(new ExifEntry("Taken", "2024-09-26 18:32:17"), info.ToKeyValuePairs());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseMetadata_DropsUndefinedColorSpaceInsteadOfShowingIt()
    {
        // 0xFFFF is what Apple writes on essentially every HEIC and Display-P3 JPEG.
        var path = ExifJpegBuilder.Write(colorSpace: 0xFFFF);

        try
        {
            var info = MetadataProcessor.ParseMetadata(path);

            Assert.DoesNotContain(info.ToKeyValuePairs(), entry => entry.Key == "Color Space");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseMetadata_KeepsARecognizedColorSpace()
    {
        var path = ExifJpegBuilder.Write(colorSpace: 1); // sRGB

        try
        {
            var info = MetadataProcessor.ParseMetadata(path);

            Assert.Contains(info.ToKeyValuePairs(), entry => entry.Key == "Color Space" && entry.Value == "sRGB");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseMetadata_FallsBackToTheDigitizedTimeWhenTheOriginalIsMissing()
    {
        var path = ExifJpegBuilder.Write(dateTimeOriginal: null, dateTimeDigitized: "2023:01:02 03:04:05");

        try
        {
            Assert.Contains(new ExifEntry("Taken", "2023-01-02 03:04:05"),
                MetadataProcessor.ParseMetadata(path).ToKeyValuePairs());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseMetadata_FallsBackToIfd0DateTimeWhenNeitherCaptureTimeIsPresent()
    {
        var path = ExifJpegBuilder.Write(dateTimeOriginal: null, dateTime: "2022:11:12 13:14:15");

        try
        {
            Assert.Contains(new ExifEntry("Taken", "2022-11-12 13:14:15"),
                MetadataProcessor.ParseMetadata(path).ToKeyValuePairs());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseMetadata_PrefersTheOriginalOverTheRestOfTheChain()
    {
        var path = ExifJpegBuilder.Write(
            dateTimeOriginal: "2024:09:26 18:32:17",
            dateTimeDigitized: "2023:01:02 03:04:05",
            dateTime: "2022:11:12 13:14:15");

        try
        {
            Assert.Contains(new ExifEntry("Taken", "2024-09-26 18:32:17"),
                MetadataProcessor.ParseMetadata(path).ToKeyValuePairs());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseMetadata_AddsNoTakenRowWhenTheFileHasNoTimestampAtAll()
    {
        var path = ExifJpegBuilder.Write(dateTimeOriginal: null);

        try
        {
            Assert.DoesNotContain(MetadataProcessor.ParseMetadata(path).ToKeyValuePairs(),
                entry => entry.Key == "Taken");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseMetadata_ShowsExposureBiasOnlyWhenItIsNotZero()
    {
        var neutral = ExifJpegBuilder.Write(exposureBias: new ExifJpegBuilder.ExposureBias(0, 1));
        var adjusted = ExifJpegBuilder.Write(exposureBias: new ExifJpegBuilder.ExposureBias(-3, 3)); // -1 EV

        try
        {
            Assert.DoesNotContain(MetadataProcessor.ParseMetadata(neutral).ToKeyValuePairs(),
                entry => entry.Key == "Exposure Bias");

            Assert.Contains(MetadataProcessor.ParseMetadata(adjusted).ToKeyValuePairs(),
                entry => entry.Key == "Exposure Bias");
        }
        finally
        {
            File.Delete(neutral);
            File.Delete(adjusted);
        }
    }

    [Fact]
    public void ParseMetadata_ReadsTiffCompressionFromIfd0()
    {
        // Compression is an IFD0 tag; a TIFF has no Exif SubIFD to read it from, so reading only
        // the SubIFD dropped the row for every TIFF.
        var path = ExifJpegBuilder.WriteTiff(compression: 5); // LZW

        try
        {
            Assert.Contains(new ExifEntry("Compression", "LZW"),
                MetadataProcessor.ParseMetadata(path).ToKeyValuePairs());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("plain text that is long enough to be cut somewhere in the middle of it")]
    [InlineData("😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀😀")]
    [InlineData("mixed 😀 content 😀 with 😀 emoji 😀 scattered 😀 through 😀 the 😀 whole 😀 string")]
    public void Truncate_NeverCutsASurrogatePairInHalf(string source)
    {
        // Every cut length, so the pair-splitting offsets are all covered.
        for (var maxLength = 1; maxLength <= source.Length + 1; maxLength++)
        {
            var result = MetadataValues.Truncate(source, maxLength);

            Assert.False(result.Any(char.IsSurrogate) && !HasOnlyPairedSurrogates(result),
                $"Lone surrogate after truncating to {maxLength}: '{result}'");
        }
    }

    private static bool HasOnlyPairedSurrogates(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    return false;

                i++;
            }
            else if (char.IsLowSurrogate(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    [Fact]
    public void ParseMetadata_FillsBitDepthAndColorTypeForJpeg()
    {
        // Both rows used to be reachable only for PNG, despite JPEG carrying the same facts.
        var path = ExifJpegBuilder.Write();

        try
        {
            var rows = MetadataProcessor.ParseMetadata(path).ToKeyValuePairs();

            Assert.Contains(new ExifEntry("Bits Per Sample", "8 bits"), rows);
            Assert.Contains(new ExifEntry("Color Type", "YCbCr"), rows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseMetadata_DropsColorSpaceWhenAnIccProfileNamesItBetter()
    {
        // EXIF says sRGB while an embedded ICC profile says Display P3. The profile is
        // authoritative, and two rows disagreeing about color helps nobody.
        var path = ExifJpegBuilder.Write(colorSpace: 1, wideGamut: true);

        try
        {
            var rows = MetadataProcessor.ParseMetadata(path).ToKeyValuePairs();

            Assert.Contains(rows, entry => entry.Key == "ICC Profile" && entry.Value.Contains("P3"));
            Assert.DoesNotContain(rows, entry => entry.Key == "Color Space");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseMetadata_ReadsTheExifIfdOfARawWhoseImageSubIfdComesFirst()
    {
        // A DNG's raw data is a SubIFD (330) listed before the EXIF pointer (34665), and
        // MetadataExtractor types both the same, so the first one is the wrong one.
        var dng = DngBuilder.Build(new DngBuilder.Spec(PreviewWidth: 16, PreviewHeight: 12, Exif:
        [
            TiffWriter.Rational(33434, 1, 250),                // ExposureTime
            TiffWriter.Shorts(34855, 400),                     // ISO
            TiffWriter.Ascii(36867, "2024:09:26 18:32:17")     // DateTimeOriginal
        ]));

        using var stream = new MemoryStream(dng, writable: false);
        var rows = MetadataProcessor.ParseMetadata(stream, "synthetic.dng").ToKeyValuePairs();

        Assert.Contains(new ExifEntry("Exposure Time", "1/250 sec"), rows);
        Assert.Contains(new ExifEntry("ISO", "400"), rows);
        Assert.Contains(new ExifEntry("Taken", "2024-09-26 18:32:17"), rows);
    }

    [Fact]
    public void ABigTiffsOrientationReachesTheRecordTheViewerReads()
    {
        var file = Build(little: false, big: true, [Short(ExifTags.Orientation, 8), Pointer(TiffDirectoryReader.TagExifIfd, Rational(ExifTags.ExposureTime, R(1, 60)))]);

        using var stream = new MemoryStream(file, writable: false);
        var info = MetadataProcessor.ParseMetadata(stream, "synthetic.tif");

        Assert.Equal(ExifOrientation.Rotate270Cw, info.OrientationValue);
        Assert.Contains(new ExifEntry("Exposure Time", "1/60 sec"), info.ToKeyValuePairs());
    }

    [Fact]
    public void AnRw2IsReadFromTheFullExifOfTheJpegItCarries()
    {
        // The RW2's own EXIF directory is sparse; the camera JPEG in tag 0x2E has the rest.
        var cameraExif = Build(
        [
            Ascii(ExifTags.Make, "Panasonic"),
            Pointer(TiffDirectoryReader.TagExifIfd, Short(ExifTags.IsoEquivalent, 3200), Short(ExifTags.WhiteBalanceMode, 0))
        ]);

        var rw2 = Build(
        [
            Ascii(ExifTags.Make, "Panasonic"),
            Undefined(0x002E, JpegWithExif(cameraExif)),
            Pointer(TiffDirectoryReader.TagExifIfd, Rational(ExifTags.ExposureTime, R(1, 60)))
        ]);
        rw2[2] = 0x55;

        using var stream = new MemoryStream(rw2, writable: false);
        var rows = MetadataProcessor.ParseMetadata(stream, "synthetic.rw2").ToKeyValuePairs();

        Assert.Contains(new ExifEntry("ISO", "3200"), rows);
        Assert.Contains(new ExifEntry("White Balance", "Auto white balance"), rows);
        Assert.Contains(new ExifEntry("Exposure Time", "1/60 sec"), rows);
    }

    private static byte[] JpegWithExif(byte[] tiff)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.Teal);
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        var jpeg = data.ToArray();

        byte[] payload = [.. "Exif\0\0"u8, .. tiff];
        var length = payload.Length + 2;
        return [.. jpeg[..2], 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload, .. jpeg[2..]];
    }
}
