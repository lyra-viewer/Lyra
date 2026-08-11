using Lyra.UI.Components.Primitives;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Layout;

/// <summary>
/// Images stretch their content to whatever bounds they are arranged at, so the size they
/// report has to be one that fits. Reporting the intrinsic size unconditionally meant an
/// oversized image overflowed its slot, and - once a parent clamped one axis via MaxWidth
/// or a Fixed sibling - rendered visibly squashed.
/// </summary>
public class ImageSizingTests
{
    private static Image Img(float w, float h) => new(w, h);

    [Fact]
    public void AnImageThatFitsKeepsItsIntrinsicSize()
    {
        var image = Img(40, 20);

        image.Measure(new SKSize(100, 100));

        Assert.Equal(new SKSize(40, 20), image.DesiredSize);
    }

    [Fact]
    public void AWideImageScalesDownOnBothAxes()
    {
        var image = Img(200, 100);

        image.Measure(new SKSize(100, 1000));

        // Halved horizontally, so halved vertically too - 2:1 preserved.
        Assert.Equal(100f, image.DesiredSize.Width, 3);
        Assert.Equal(50f, image.DesiredSize.Height, 3);
    }

    [Fact]
    public void ATallImageScalesDownOnBothAxes()
    {
        var image = Img(100, 200);

        image.Measure(new SKSize(1000, 50));

        Assert.Equal(25f, image.DesiredSize.Width, 3);
        Assert.Equal(50f, image.DesiredSize.Height, 3);
    }

    [Fact]
    public void TheTighterAxisWins()
    {
        var image = Img(200, 200);

        image.Measure(new SKSize(100, 40));

        Assert.Equal(40f, image.DesiredSize.Width, 3);
        Assert.Equal(40f, image.DesiredSize.Height, 3);
    }

    [Fact]
    public void AspectRatioSurvivesScaling()
    {
        var image = Img(300, 100);

        image.Measure(new SKSize(90, 1000));

        var ratio = image.DesiredSize.Width / image.DesiredSize.Height;
        Assert.Equal(3f, ratio, 3);
    }

    [Fact]
    public void AnUnboundedOfferLeavesTheImageAlone()
    {
        // ListView measures rows with float.MaxValue height.
        var image = Img(64, 64);

        image.Measure(new SKSize(float.MaxValue, float.MaxValue));

        Assert.Equal(new SKSize(64, 64), image.DesiredSize);
    }

    [Fact]
    public void ANonPositiveOfferIsTreatedAsUnconstrained()
    {
        // A transient pass with no space left must not collapse icons permanently.
        var image = Img(16, 16);

        image.Measure(new SKSize(0, 0));

        Assert.Equal(new SKSize(16, 16), image.DesiredSize);
    }

    [Fact]
    public void AnImageWithNoIntrinsicSizeMeasuresEmpty()
    {
        Assert.Equal(SKSize.Empty, Img(0, 0).Measure(new SKSize(100, 100)));
        Assert.Equal(SKSize.Empty, Img(-5, 10).Measure(new SKSize(100, 100)));
    }
}