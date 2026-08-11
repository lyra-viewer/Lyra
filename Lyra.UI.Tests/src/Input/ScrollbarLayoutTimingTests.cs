using Lyra.UI.Components;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Input;

/// <summary>
/// The bar's hit region has to be published during Arrange, not during Draw.
///
/// Pointer events arrive between frames. A bar that only positions itself while drawing
/// answers Contains() with wherever it was on the previous frame, so the first click after
/// a panel resizes or a section expands lands in the old place - or nowhere, if the bar
/// did not exist yet.
/// </summary>
public class ScrollbarLayoutTimingTests
{
    private const float Width = 400f;
    private const float Height = 200f;
    private const float BarX = 394f;

    private static VScrollContainer Scroller(float contentHeight)
    {
        var scroller = new VScrollContainer
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Expand
        };

        scroller.AddComponent(new HStack
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Fixed,
            Height = contentHeight
        });

        return scroller;
    }

    private static void Layout(IComponent root, float height = Height)
    {
        root.Measure(new SKSize(Width, height));
        root.Resolve();
        root.Arrange(new SKRect(0, 0, Width, height));
    }

    [Fact]
    public void TheBarIsGrabbableAfterLayoutWithoutHavingRendered()
    {
        var scroller = Scroller(1000f);

        Layout(scroller);

        // No Render call at all - the very first frame's input must still land.
        Assert.True(((IScrollable)scroller).ScrollbarContains(new SKPoint(BarX, 100f)));

        scroller.OnPointerDown(new SKPoint(BarX, 20f));
        scroller.OnPointerMove(new SKPoint(BarX, 120f));

        Assert.NotEqual(0f, scroller.ScrollOffset);
    }

    [Fact]
    public void TheBarStopsBeingGrabbableAsSoonAsTheContentFits()
    {
        var scroller = Scroller(1000f);
        Layout(scroller);
        Assert.True(((IScrollable)scroller).ScrollbarContains(new SKPoint(BarX, 100f)));

        // Content shrinks below the viewport: the bar should be gone immediately,
        // not one frame later.
        scroller.Clear();
        scroller.AddComponent(new HStack
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Fixed,
            Height = 50f
        });
        Layout(scroller);

        Assert.False(((IScrollable)scroller).ScrollbarContains(new SKPoint(BarX, 100f)));
    }

    [Fact]
    public void TheBarTracksAViewportThatGrows()
    {
        var scroller = Scroller(1000f);
        Layout(scroller, height: 100f);

        // Track spans the viewport, so a point low in the taller viewport is off the
        // short bar and on the tall one.
        var lowPoint = new SKPoint(BarX, 180f);
        Assert.False(((IScrollable)scroller).ScrollbarContains(lowPoint));

        Layout(scroller, height: 200f);

        Assert.True(((IScrollable)scroller).ScrollbarContains(lowPoint));
    }
}
