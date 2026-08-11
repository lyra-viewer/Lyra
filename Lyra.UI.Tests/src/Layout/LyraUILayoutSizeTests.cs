using Lyra.UI;
using Lyra.UI.Components;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Layout;

/// <summary>
/// LyraUI.Process must lay out at the exact logical size of the surface.
///
/// Skia's LocalClipBounds outsets the device clip by 1px per side before mapping
/// it to local space (a 400x200 surface reports {-1,-1,402x202} at identity), so
/// deriving the layout size from it pushes right/bottom-anchored elements - the
/// sidebar edge, the status layer, the scrollbar track - off-screen.
/// </summary>
public class LyraUILayoutSizeTests
{
    private sealed class ProbeComponent : ComponentBase
    {
        public SKSize MeasuredWith { get; private set; }

        protected override SKSize MeasureContent(SKSize availableSize)
        {
            MeasuredWith = availableSize;
            return availableSize;
        }

        protected override void ArrangeContent(SKRect contentBounds) { }
        protected override void RenderContent(SKCanvas canvas, SKRect contentBounds) { }
    }

    [Theory]
    [InlineData(1f, 400f, 200f)]
    [InlineData(2f, 200f, 100f)]
    [InlineData(1.5f, 266.66667f, 133.33334f)]
    public void ArrangesRootAtExactLogicalSurfaceSize(float contentScale, float expectedWidth, float expectedHeight)
    {
        using var surface = SKSurface.Create(new SKImageInfo(400, 200));
        var canvas = surface.Canvas;

        // Mirrors SkiaRendererBase.RenderUI.
        canvas.Scale(contentScale);

        var probe = new ProbeComponent();
        using var context = new UIContext();
        context.AddLayer("Main").Root = probe;
        context.Invalidate();

        LyraUI.Process(context, canvas);

        Assert.Equal(expectedWidth, probe.MeasuredWith.Width, 3);
        Assert.Equal(expectedHeight, probe.MeasuredWith.Height, 3);

        Assert.Equal(0f, probe.ArrangedBounds.Left);
        Assert.Equal(0f, probe.ArrangedBounds.Top);
        Assert.Equal(expectedWidth, probe.ArrangedBounds.Width, 3);
        Assert.Equal(expectedHeight, probe.ArrangedBounds.Height, 3);
    }

    [Fact]
    public void RightAnchoredContentStaysInsideTheSurface()
    {
        using var surface = SKSurface.Create(new SKImageInfo(400, 200));
        var canvas = surface.Canvas;
        canvas.Scale(2f);

        var probe = new ProbeComponent();
        using var context = new UIContext();
        context.AddLayer("Main").Root = probe;
        context.Invalidate();

        LyraUI.Process(context, canvas);

        // A scrollbar drawn at Right - Width - Margin must keep its full margin.
        const float width = 6f;
        const float margin = 2f;
        var trackRight = probe.ArrangedBounds.Right - margin;

        Assert.True(trackRight <= 200f, $"track right edge {trackRight} overhangs the 200pt logical surface");
        Assert.Equal(198f, trackRight);
        Assert.Equal(192f, trackRight - width);
    }
}