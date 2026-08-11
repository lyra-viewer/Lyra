using Lyra.UI.Components;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Controls;

public class LabelEllipsisTests
{
    private const string Long = "a directory name far too long for the panel";

    private static Label Wide(string text = Long) => new(text) { Ellipsize = true };

    /// <summary>Renders at a given width and reports the pixels actually touched.</summary>
    private static float PaintedWidth(Label label, float width)
    {
        const int height = 30;
        var w = (int)MathF.Ceiling(width) + 40;

        using var surface = SKSurface.Create(new SKImageInfo(w, height));
        surface.Canvas.Clear(SKColors.Black);

        label.Measure(new SKSize(width, height));
        label.Arrange(new SKRect(0, 0, width, height));
        label.Render(surface.Canvas);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);

        var rightmost = -1;
        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < height; y++)
            {
                if (bitmap.GetPixel(x, y) == SKColors.Black)
                    continue;

                rightmost = x;
                break;
            }
        }

        return rightmost + 1;
    }

    // --------------------------------------------------------
    //  Measure is unaffected
    // --------------------------------------------------------

    [Fact]
    public void MeasureStillReportsTheFullTextWidth()
    {
        var plain = new Label(Long);
        var ellipsize = Wide();

        var plainSize = plain.Measure(new SKSize(50, 30));
        var ellipsisSize = ellipsize.Measure(new SKSize(50, 30));

        Assert.Equal(plainSize.Width, ellipsisSize.Width, 3);
    }

    [Fact]
    public void AShrinkLabelIsGivenItsFullWidthAndNeverTruncates()
    {
        var label = Wide();
        var row = new HStack { HorizontalSize = SizeMode.Expand };
        row.AddComponent(label);

        row.Measure(new SKSize(60, 30));
        row.Resolve();
        row.Arrange(new SKRect(0, 0, 60, 30));

        Assert.True(label.ArrangedBounds.Width > 60f);
    }

    // --------------------------------------------------------
    //  Rendering stays inside the bounds
    // --------------------------------------------------------

    [Fact]
    public void TextTooLongForItsBoundsIsCutToFit()
    {
        const float width = 80f;
        var painted = PaintedWidth(Wide(), width);

        Assert.True(painted > 0, "nothing was drawn");
        Assert.True(painted <= width + 1f, $"painted {painted}px, bounds are {width}px");
    }

    [Fact]
    public void WithoutEllipsizeTheTextOverflows()
    {
        // The behavior ellipsis exists to replace - kept as the contrast case.
        const float width = 80f;
        var painted = PaintedWidth(new Label(Long), width);

        Assert.True(painted > width + 1f, $"expected overflow past {width}px, painted {painted}px");
    }

    [Fact]
    public void TextThatFitsIsLeftAlone()
    {
        var label = Wide("ab");

        var narrow = PaintedWidth(label, 200f);
        var plain = PaintedWidth(new Label("ab"), 200f);

        Assert.Equal(plain, narrow);
    }

    [Fact]
    public void AWidthTooSmallForEvenTheMarkerDrawsNothing()
    {
        var painted = PaintedWidth(Wide(), 1f);

        Assert.Equal(0f, painted);
    }

    [Fact]
    public void TheCutFollowsTheWidthWhenItChanges()
    {
        var label = Wide();

        var atSixty = PaintedWidth(label, 60f);
        var atOneEighty = PaintedWidth(label, 180f);

        Assert.True(atSixty <= 61f);
        Assert.True(atOneEighty <= 181f);
        Assert.True(atOneEighty > atSixty, "a wider label should show more text");
    }

    [Fact]
    public void TheCutFollowsTheTextWhenItChanges()
    {
        var label = Wide();
        var first = PaintedWidth(label, 100f);

        label.Text = "short";
        var second = PaintedWidth(label, 100f);

        Assert.True(second < first, "a shorter string should paint less");
    }
}