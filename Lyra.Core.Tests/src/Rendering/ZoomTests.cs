using System.Globalization;
using Lyra.Common;
using Lyra.SdlCore;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

public class ZoomTests
{
    // A0 (841x1189 mm) scanned at 2000 dpi.
    private const int A0Width = 66220;
    private const int A0Height = 93620;

    [Fact]
    public void AnA0SheetAt2000DpiFitsTheWindowItWasFittedTo()
    {
        const int windowWidth = 1600;
        const int windowHeight = 1000;

        var zoom = DimensionHelper.GetZoomToFitScreen(A0Width, A0Height, windowWidth, windowHeight, contentScale: 1f);

        Assert.Equal(windowHeight, A0Height * (zoom / 100f), 0.5);
        Assert.True(A0Width * (zoom / 100f) <= windowWidth);
    }

    [Fact]
    public void FitNeverOverflowsTheWindowWhenTheRoundedZoomWouldHave()
    {
        // 1.538% fits; the 2% it used to round to draws the sheet 1872px tall in a 1440px window.
        const int windowWidth = 2560;
        const int windowHeight = 1440;

        var zoom = DimensionHelper.GetZoomToFitScreen(A0Width, A0Height, windowWidth, windowHeight, contentScale: 1f);

        Assert.True(A0Width * (zoom / 100f) <= windowWidth);
        Assert.True(A0Height * (zoom / 100f) <= windowHeight);
        Assert.Equal(windowHeight, A0Height * (zoom / 100f), 0.5);
    }

    [Fact]
    public void FitIsMeasuredInLogicalUnitsOnAScaledDisplay()
    {
        var scaled = DimensionHelper.GetZoomToFitScreen(A0Width, A0Height, 2560, 1600, contentScale: 2f);
        var unscaled = DimensionHelper.GetZoomToFitScreen(A0Width, A0Height, 1280, 800, contentScale: 1f);

        Assert.Equal(unscaled, scaled, 5);
    }

    [Theory]
    [InlineData(0, 100, 1f)]
    [InlineData(100, 0, 1f)]
    [InlineData(100, 100, 0f)]
    public void FitFallsBackToActualSizeOnDegenerateInput(int width, int height, float contentScale)
    {
        Assert.Equal(DimensionHelper.ActualSize, DimensionHelper.GetZoomToFitScreen(width, height, 800, 600, contentScale));
    }

    [Fact]
    public void SteppingMakesProgressBelowOnePercent()
    {
        var zoom = 1.07f;

        for (var i = 0; i < 20; i++)
        {
            var next = DimensionHelper.GetNextZoom(zoom, -1);
            Assert.True(next < zoom);
            zoom = next;
        }

        Assert.True(zoom > DimensionHelper.MinZoom);
    }

    [Fact]
    public void SteppingIsSymmetricAndClamped()
    {
        Assert.Equal(100f, DimensionHelper.GetNextZoom(DimensionHelper.GetNextZoom(100f, +1), -1), 3);

        Assert.Equal(DimensionHelper.MaxZoom, DimensionHelper.GetNextZoom(DimensionHelper.MaxZoom, +1));
        Assert.Equal(DimensionHelper.MinZoom, DimensionHelper.GetNextZoom(DimensionHelper.MinZoom, -1));
    }

    [Fact]
    public void SteppingOverTheFitZoomLandsOnIt()
    {
        // The step is multiplicative, so it can jump across the fit zoom without ever landing on
        // it - scrolling back down from a free zoom would otherwise skip fit entirely.
        const float fit = 1.4815f;

        Assert.True(DimensionHelper.ReachesZoomToFit(1.5556f, 1.4815f * 0.999f, fit));
        Assert.True(DimensionHelper.ReachesZoomToFit(1.41f, 1.49f, fit));
    }

    [Fact]
    public void AStepThatStopsShortOfTheFitZoomIsStillFit()
    {
        // Stepping in and back out again does not return the exact float it started from.
        const float fit = 1.4815f;
        var away = DimensionHelper.GetNextZoom(fit, +1);
        var back = DimensionHelper.GetNextZoom(away, -1);

        Assert.True(DimensionHelper.ReachesZoomToFit(away, back, fit));
    }

    [Fact]
    public void FitCanStillBeLeft()
    {
        const float fit = 1.4815f;

        Assert.False(DimensionHelper.ReachesZoomToFit(fit, DimensionHelper.GetNextZoom(fit, +1), fit));
        Assert.False(DimensionHelper.ReachesZoomToFit(fit, DimensionHelper.GetNextZoom(fit, -1), fit));
    }

    [Fact]
    public void AStepThatDoesNotReachTheFitZoomIsNotSnapped()
    {
        const float fit = 1.4815f;

        Assert.False(DimensionHelper.ReachesZoomToFit(100f, 105f, fit));
        Assert.False(DimensionHelper.ReachesZoomToFit(2f, 1.9f, fit));
    }

    [Theory]
    [InlineData(0.4f, "0.4%")]
    [InlineData(0.96f, "1.0%")]
    [InlineData(1f, "1.0%")]
    [InlineData(1.068f, "1.1%")]
    [InlineData(1.4815f, "1.5%")]
    [InlineData(9.96f, "10.0%")]
    [InlineData(10f, "10.0%")]
    [InlineData(15.7625f, "16%")]
    [InlineData(100f, "100%")]
    [InlineData(10000f, "10000%")]
    public void ZoomIsShownWithADecimalOnlyWhereAWholePercentWouldSayNothing(float zoom, string expected)
    {
        // The info panel formats numbers in the user's culture, as SizeToStr already does.
        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

        Assert.Equal(expected.Replace(".", separator), Formatters.ZoomToStr(zoom));
    }
}