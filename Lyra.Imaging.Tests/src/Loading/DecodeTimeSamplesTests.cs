using Lyra.Common.Estimation;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Loading;

public class DecodeTimeSamplesTests
{
    private const string Png = ".png";
    private const string Jpeg = ".jpg";
    private const string Tiff = ".tif";
    private const long OneMb = 1024 * 1024;
    
    [Fact]
    public void OneOutlierDoesNotDragTheEstimate()
    {
        var samples = NewSamples();

        for (var i = 0; i < 9; i++)
            samples.Record(Png, OneMb, null, 100);

        samples.Record(Png, OneMb, null, 30_000); // A stalled read.

        var estimate = samples.Estimate(Png, OneMb).Ms;

        // The mean of these is ~3090. The median is unmoved.
        Assert.Equal(100, estimate, precision: 6);
    }

    [Fact]
    public void AnUnseenFormatHasNoEstimate()
    {
        var estimate = NewSamples().Estimate(Png, OneMb).Ms;

        Assert.False(estimate > 0);
        Assert.Equal(0, estimate);
    }
    
    [Fact]
    public void AnUnseenSizeIsScaledFromTheNearestBucketOfTheSameFormat()
    {
        var samples = NewSamples();
        samples.Record(Png, OneMb, null, 150);

        // 1 MB and 64 MB are six doublings apart, so the estimate is 2^6 times the sample.
        var estimate = samples.Estimate(Png, 64 * OneMb).Ms;

        Assert.Equal(150 * 64, estimate, precision: 6);
    }

    [Fact]
    public void ScalingWorksDownwardsAsWellAsUpwards()
    {
        var samples = NewSamples();
        samples.Record(Png, 64 * OneMb, null, 9600);

        Assert.Equal(150, samples.Estimate(Png, OneMb).Ms, precision: 6);
    }
    
    [Fact]
    public void TwoFilesOfEqualSizeAreToldApartByTheirPixelCounts()
    {
        var samples = NewSamples();

        samples.Record(Png, OneMb, 200_000_000, 3000); // A flat gradient that compresses to nothing.
        samples.Record(Png, OneMb, 500_000, 40);       // A photograph of the same weight.

        Assert.Equal(3000, samples.Estimate(Png, OneMb, 200_000_000).Ms, precision: 6);
        Assert.Equal(40, samples.Estimate(Png, OneMb, 500_000).Ms, precision: 6);
    }
    
    [Fact]
    public void PixelsAnswerInPreferenceToBytes()
    {
        var samples = NewSamples();
        samples.Record(Png, OneMb, 4_000_000, 500);

        // Twenty times the file, the same picture: the pixel bucket matches exactly and answers.
        Assert.Equal(500, samples.Estimate(Png, 20 * OneMb, 4_000_000).Ms, precision: 6);
    }
    
    [Fact]
    public void BytesStillAnswerWhenThePixelCountIsUnknown()
    {
        var samples = NewSamples();
        samples.Record(Png, OneMb, 4_000_000, 500);

        Assert.Equal(500, samples.Estimate(Png, OneMb).Ms, precision: 6);
    }
    
    [Fact]
    public void ARecordWithoutPixelsFeedsOnlyTheByteHistory()
    {
        using var file = new TempFile([]);

        var samples = new DecodeTimeSamples(file.Path);
        samples.Record(Png, OneMb, null, 120);
        samples.Save(suppressLogging: true);

        Assert.Equal(120, samples.Estimate(Png, OneMb).Ms, precision: 6);
        Assert.DoesNotContain("pixels", File.ReadAllText(file.Path));
    }
    
    [Fact]
    public void TheFallbackNeverCrossesFormats()
    {
        var samples = NewSamples();
        samples.Record(Jpeg, OneMb, 4_000_000, 150);

        Assert.Equal(0, samples.Estimate(Png, OneMb).Ms);
        Assert.Equal(0, samples.Estimate(Png, OneMb, 4_000_000).Ms);
    }

    [Fact]
    public void UnknownFormatsAreIgnored()
    {
        using var file = new TempFile([]);

        var samples = new DecodeTimeSamples(file.Path);
        samples.Record(".not-an-image", OneMb, 4_000_000, 150);
        samples.Save(suppressLogging: true);

        Assert.False(samples.Estimate(".not-an-image", OneMb, 4_000_000).Ms > 0);
        Assert.DoesNotContain("not-an-image", File.ReadAllText(file.Path));
    }
    
    [Fact]
    public void SamplesSurviveARoundTrip()
    {
        using var file = new TempFile([]);

        var written = new DecodeTimeSamples(file.Path);
        written.Record(Png, OneMb, 4_000_000, 120);
        written.Record(Jpeg, 8 * OneMb, 24_000_000, 900);
        written.Save(suppressLogging: true);

        var read = new DecodeTimeSamples(file.Path);

        Assert.Equal(120, read.Estimate(Png, OneMb).Ms, precision: 6);
        Assert.Equal(120, read.Estimate(Png, OneMb, 4_000_000).Ms, precision: 6);
        Assert.Equal(900, read.Estimate(Jpeg, 8 * OneMb).Ms, precision: 6);
        Assert.Equal(900, read.Estimate(Jpeg, 8 * OneMb, 24_000_000).Ms, precision: 6);
    }
    
    [Theory]
    // No version key at all.
    [InlineData("[png]\n8 = [120, 130, 140]\n")]
    // Byte buckets only, no pixel-keyed half.
    [InlineData("version = 2\n\n[png]\n8 = [120, 130, 140]\n")]
    // Decode-only tables, before whole-load samples existed.
    [InlineData("version = 3\n\n[png.bytes]\n8 = [120]\n\n[png.pixels]\n64 = [120]\n")]
    // And a version from a build newer than this one, whose tables we cannot know.
    [InlineData("version = 99\n\n[png.bytes]\n8 = [120]\n")]
    public void OnlyTheCurrentSchemaIsRead(string contents)
    {
        using var file = new TempFile([]);
        File.WriteAllText(file.Path, contents);

        var samples = new DecodeTimeSamples(file.Path);

        Assert.Equal(0, samples.Estimate(Png, OneMb).Ms);
        Assert.Equal(0, samples.Estimate(Png, OneMb, 4_000_000).Ms);
    }

    [Fact]
    public void DiscardingAnOlderFileLeavesAUsableHistory()
    {
        using var file = new TempFile([]);
        File.WriteAllText(file.Path, "version = 2\n\n[png]\n8 = [120]\n");

        var samples = new DecodeTimeSamples(file.Path);
        samples.Record(Png, OneMb, 4_000_000, 55);
        samples.Save(suppressLogging: true);

        Assert.Equal(55, new DecodeTimeSamples(file.Path).Estimate(Png, OneMb, 4_000_000).Ms, precision: 6);
    }

    [Fact]
    public void AnUnparseableFileLeavesAnEmptyHistory()
    {
        using var file = new TempFile([]);
        File.WriteAllText(file.Path, "this is not [ valid toml = = =\n");

        var samples = new DecodeTimeSamples(file.Path);

        Assert.Equal(0, samples.Estimate(Png, OneMb).Ms);

        // And it still works from there.
        samples.Record(Png, OneMb, 4_000_000, 55);
        Assert.Equal(55, samples.Estimate(Png, OneMb, 4_000_000).Ms, precision: 6);
    }
    
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveDurationsAreNotRecorded(double ms)
    {
        var samples = NewSamples();
        samples.Record(Png, OneMb, 4_000_000, ms);

        Assert.Equal(0, samples.Estimate(Png, OneMb, 4_000_000).Ms);
    }
    
    [Fact]
    public void AnAbsurdPixelCountIsBoundedRatherThanBelieved()
    {
        var samples = NewSamples();
        samples.Record(Png, OneMb, 4_000_000, 500);

        var estimate = samples.Estimate(Png, OneMb, long.MaxValue / 2).Ms;

        Assert.True(estimate > 0);
        Assert.True(estimate <= 10 * 60 * 1000);
    }
    
    [Fact]
    public void TheHistoryRollsForwardAndForgetsOldSamples()
    {
        var samples = NewSamples();

        for (var i = 0; i < 25; i++)
            samples.Record(Png, OneMb, 4_000_000, 1000);

        for (var i = 0; i < 25; i++)
            samples.Record(Png, OneMb, 4_000_000, 50);

        Assert.Equal(50, samples.Estimate(Png, OneMb, 4_000_000).Ms, precision: 6);
    }

    // ------------------------------------------------------------------
    //  Loads whose read cannot be told apart from their decode
    // ------------------------------------------------------------------
    
    [Fact]
    public void ALoadWhoseReadCannotBeTimedIsStillLearnedFrom()
    {
        var samples = NewSamples();
        samples.Record(Tiff, 4_000_000_000, 1_436_000_000, 2660, includesTransfer: true);

        var estimate = samples.Estimate(Tiff, 4_000_000_000, 1_436_000_000);

        Assert.Equal(2660, estimate.Ms, precision: 6);
        Assert.True(estimate.IsKnown);
    }
    
    [Fact]
    public void AWholeLoadEstimateSaysSo()
    {
        var samples = NewSamples();
        samples.Record(Tiff, 4_000_000_000, 1_436_000_000, 2660, includesTransfer: true);

        Assert.True(samples.Estimate(Tiff, 4_000_000_000, 1_436_000_000).IncludesTransfer);
    }

    [Fact]
    public void ADecodeEstimateIsNotMarked()
    {
        var samples = NewSamples();
        samples.Record(Tiff, OneMb, 4_000_000, 120);

        Assert.False(samples.Estimate(Tiff, OneMb, 4_000_000).IncludesTransfer);
    }
    
    [Fact]
    public void TheTwoKindsNeverMix()
    {
        var samples = NewSamples();

        samples.Record(Tiff, OneMb, 4_000_000, 100);
        samples.Record(Tiff, OneMb, 4_000_000, 9000, includesTransfer: true);

        var estimate = samples.Estimate(Tiff, OneMb, 4_000_000);

        Assert.Equal(100, estimate.Ms, precision: 6);
        Assert.False(estimate.IncludesTransfer);
    }
    
    [Fact]
    public void TheSizeDecidesWhichKindAnswers()
    {
        var samples = NewSamples();

        samples.Record(Tiff, OneMb, 4_000_000, 100);                                      // small: measured
        samples.Record(Tiff, 4_000_000_000, 1_436_000_000, 2660, includesTransfer: true); // large: streamed

        var small = samples.Estimate(Tiff, OneMb, 4_000_000);
        var large = samples.Estimate(Tiff, 4_000_000_000, 1_436_000_000);

        Assert.False(small.IncludesTransfer);
        Assert.Equal(100, small.Ms, precision: 6);

        Assert.True(large.IncludesTransfer);
        Assert.Equal(2660, large.Ms, precision: 6);
    }

    [Fact]
    public void BothKindsSurviveARoundTrip()
    {
        using var file = new TempFile([]);

        var written = new DecodeTimeSamples(file.Path);
        written.Record(Tiff, OneMb, 4_000_000, 100);
        written.Record(Tiff, 4_000_000_000, 1_436_000_000, 2660, includesTransfer: true);
        written.Save(suppressLogging: true);

        var read = new DecodeTimeSamples(file.Path);

        Assert.False(read.Estimate(Tiff, OneMb, 4_000_000).IncludesTransfer);
        Assert.True(read.Estimate(Tiff, 4_000_000_000, 1_436_000_000).IncludesTransfer);
    }

    [Fact]
    public void SavingToAnUnwritablePathIsSwallowed()
    {
        var samples = new DecodeTimeSamples(Path.Combine(Path.GetTempPath(), "lyra-test-nul\0bad", "x.toml"));
        samples.Record(Png, OneMb, 4_000_000, 100);

        samples.Save(suppressLogging: true);
    }

    private static DecodeTimeSamples NewSamples() => new(Path.Combine(Path.GetTempPath(), $"lyra-test-{Guid.NewGuid():N}.toml"));
}
