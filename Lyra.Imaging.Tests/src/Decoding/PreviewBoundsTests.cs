using Lyra.Imaging.Decoding.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// How large a preview may be, when the display it is sized against may not be known yet.
/// </summary>
public class PreviewBoundsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1440)]
    [InlineData(2560, 0)]
    [InlineData(-1, -1)]
    public void AnUnpublishedDisplayStillGivesAPreviewSomethingToFit(int logicalWidth, int logicalHeight)
    {
        var (width, height) = RasterContentBuilder.PreviewBounds(logicalWidth, logicalHeight);

        Assert.True(width > 0);
        Assert.True(height > 0);
    }

    [Fact]
    public void APublishedDisplayIsUsedWithHeadroomForZoom()
    {
        var (width, height) = RasterContentBuilder.PreviewBounds(2560, 1440);

        Assert.Equal(5120, width);
        Assert.Equal(2880, height);
    }

    [Fact]
    public void OnlyTheMissingAxisFallsBack()
    {
        // 2560 is the stand-in edge; the axis that was published keeps its own value.
        var (width, height) = RasterContentBuilder.PreviewBounds(0, 1440);

        Assert.Equal(5120, width);
        Assert.Equal(2880, height);
    }
}
