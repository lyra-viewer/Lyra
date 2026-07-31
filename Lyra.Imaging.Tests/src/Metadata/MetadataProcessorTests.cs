using Lyra.Imaging.Content;
using Lyra.Imaging.Metadata;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Metadata;

/// <summary>
/// Covers the normalization layer between MetadataExtractor and the panel: placeholder values
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

    [Theory]
    // multiLocalizedUnicode, as MetadataExtractor renders it
    [InlineData("1 enUS(Display P3)", "Display P3")]
    // a single record whose name contains parentheses: the last ')' closes it
    [InlineData("1 enUS(sRGB IEC61966-2.1 (v2))", "sRGB IEC61966-2.1 (v2)")]
    // several records: stop at the first one rather than swallowing the rest
    [InlineData("2 enUS(Display P3)deDE(Anzeige P3)", "Display P3")]
    // older profiles store plain text and arrive ready to display
    [InlineData("Adobe RGB (1998)", "Adobe RGB (1998)")]
    [InlineData("", "")]
    public void ExtractIccProfileName_UnwrapsTheLocalizedForm(string value, string expected)
    {
        Assert.Equal(expected, MetadataValues.ExtractIccProfileName(value));
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
}
