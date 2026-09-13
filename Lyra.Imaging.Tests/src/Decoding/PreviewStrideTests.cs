using Lyra.Imaging.Decoding.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// How far the preview pass steps between the samples it averages.
/// </summary>
public class PreviewStrideTests
{
    [Fact]
    public void NoPreviewPixelIsAveragedFromMoreThanTheMaximum()
    {
        for (var preview = 64; preview <= 4096; preview += 64)
        for (var source = preview; source <= preview * 64; source += Math.Max(1, preview / 8))
        {
            var stride = StreamingGrayPreview.StrideFor(source, preview);
            var samples = (double)source / preview / stride;

            Assert.True(samples <= StreamingGrayPreview.MaxSamplesPerAxis + 1e-9, $"{source}->{preview} at stride {stride} averages {samples:F2} samples an axis");
        }
    }
    
    [Fact]
    public void TheStrideIsTheTightestThatObeysTheCeiling()
    {
        for (var preview = 64; preview <= 4096; preview += 64)
        for (var source = preview * 2; source <= preview * 64; source += Math.Max(1, preview / 8))
        {
            var stride = StreamingGrayPreview.StrideFor(source, preview);
            if (stride == 1)
                continue;

            var looser = (double)source / preview / (stride - 1);

            Assert.True(looser > StreamingGrayPreview.MaxSamplesPerAxis, $"{source}->{preview} could have used stride {stride - 1} and still obeyed the ceiling");
        }
    }

    [Fact]
    public void TheRatioThatUsedToTruncateToOne()
    {
        // A 35,900-row sheet previewed at 5,760 on a 5K display.
        Assert.Equal(2, StreamingGrayPreview.StrideFor(35900, 5760));
    }

    [Theory]
    [InlineData(1000, 1000)]
    [InlineData(500, 1000)]
    [InlineData(1000, 4000)]
    public void NoDownsampleMeansNoStride(int source, int preview)
    {
        Assert.Equal(1, StreamingGrayPreview.StrideFor(source, preview));
    }

    [Theory]
    [InlineData(1000, 0)]
    [InlineData(1000, -1)]
    [InlineData(0, 100)]
    public void ADegeneratePreviewIsHarmless(int source, int preview)
    {
        Assert.Equal(1, StreamingGrayPreview.StrideFor(source, preview));
    }
}
