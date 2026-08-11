using Lyra.UI;
using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Input;

/// <summary>
/// The scrollbar is drawn inside its host rather than laid out as a child, so its geometry only
/// exists after a render pass - every test here renders a frame before touching input.
///
/// Two priority rules are load-bearing and easy to regress:
///   - the bar wins over the children drawn underneath it (rows span the full width)
///   - the bar wins over the resize-edge zone it overlaps, because mistaking a scroll drag for a
///     resize drags a panel, or a snapped window, out of shape
/// </summary>
public class ScrollbarDragTests
{
    private const float ViewportWidth = 400f;
    private const float ViewportHeight = 200f;
    private const float ContentHeight = 1000f;

    // Default style: 7 wide, 2 margin - so the track spans x 391..398 at this width,
    // and the last 5px of that overlap a Right resize zone.
    private const float BarX = 394f;
    private const float BarOverResizeEdgeX = 396f;

    // Track geometry that follows from the constants above.
    private const float TrackTop = 2f;
    private const float TrackHeight = ViewportHeight - 4f;                          // 196
    private const float ThumbHeight = TrackHeight * ViewportHeight / ContentHeight; // 39.2
    private const float ThumbTravel = TrackHeight - ThumbHeight;                    // 156.8
    private const float MaxScroll = ContentHeight - ViewportHeight;                 // 800

    // --------------------------------------------------------
    //  Dragging the thumb
    // --------------------------------------------------------

    [Fact]
    public void DraggingTheThumbScrollsProportionally()
    {
        var scroller = Scroller();
        Frame(scroller);

        scroller.OnPointerDown(new SKPoint(BarX, TrackTop));
        scroller.OnPointerMove(new SKPoint(BarX, TrackTop + ThumbTravel / 2f));

        AssertClose(MaxScroll / 2f, scroller.ScrollOffset);
    }

    [Fact]
    public void GrabbingTheThumbAnywhereKeepsItUnderTheCursor()
    {
        var scroller = Scroller();
        Frame(scroller);

        // Grab 18px below the thumb's top edge, then move the pointer half the travel.
        // A bar that re-centred the thumb on the cursor would land somewhere else.
        scroller.OnPointerDown(new SKPoint(BarX, TrackTop + 18f));
        scroller.OnPointerMove(new SKPoint(BarX, TrackTop + 18f + ThumbTravel / 2f));

        AssertClose(MaxScroll / 2f, scroller.ScrollOffset);
    }

    [Fact]
    public void DraggingPastTheEndsClampsInsteadOfOverscrolling()
    {
        var scroller = Scroller();
        Frame(scroller);

        scroller.OnPointerDown(new SKPoint(BarX, TrackTop));
        scroller.OnPointerMove(new SKPoint(BarX, 10_000f));
        AssertClose(MaxScroll, scroller.ScrollOffset);

        scroller.OnPointerMove(new SKPoint(BarX, -10_000f));
        AssertClose(0f, scroller.ScrollOffset);
    }

    [Fact]
    public void ReleasingEndsTheDrag()
    {
        var scroller = Scroller();
        Frame(scroller);

        // Drag somewhere non-zero first. Asserting "still 0" after a release would
        // also pass if the bar never moved anything to begin with.
        scroller.OnPointerDown(new SKPoint(BarX, TrackTop));
        scroller.OnPointerMove(new SKPoint(BarX, TrackTop + ThumbTravel / 4f));
        var afterDrag = scroller.ScrollOffset;
        AssertClose(MaxScroll / 4f, afterDrag);

        scroller.OnPointerUp(new SKPoint(BarX, TrackTop + ThumbTravel / 4f));

        // Moves after the release are no longer the bar's business.
        scroller.OnPointerMove(new SKPoint(BarX, TrackTop + ThumbTravel));
        Assert.Equal(afterDrag, scroller.ScrollOffset);

        scroller.OnPointerMove(new SKPoint(BarX, TrackTop));
        Assert.Equal(afterDrag, scroller.ScrollOffset);
    }

    [Fact]
    public void PressingTheTrackCentresTheThumbOnTheCursor()
    {
        var scroller = Scroller();
        Frame(scroller);

        scroller.OnPointerDown(new SKPoint(BarX, 150f));

        var expectedRatio = (150f - ThumbHeight / 2f - TrackTop) / ThumbTravel;
        AssertClose(expectedRatio * MaxScroll, scroller.ScrollOffset);
    }

    [Fact]
    public void ThereIsNoBarToGrabWhenTheContentFits()
    {
        var scroller = Scroller(contentHeight: ViewportHeight / 2f);
        Frame(scroller);

        // With nothing to scroll, ScrollOffset is pinned at 0 either way, so the
        // offset alone proves nothing. Assert the bar itself is absent.
        Assert.False(((IScrollable)scroller).NeedsScrollbar);
        Assert.False(((IScrollable)scroller).ScrollbarContains(new SKPoint(BarX, 150f)));
        Assert.Equal(0f, ((IScrollable)scroller).MaxScroll);

        scroller.OnPointerDown(new SKPoint(BarX, 150f));
        scroller.OnPointerMove(new SKPoint(BarX, 20f));

        Assert.Equal(0f, scroller.ScrollOffset);

        // Wheel input is declined, so it can bubble to the parent (or the image).
        Assert.False(((IScrollable)scroller).OnScroll(-5f));
    }

    // --------------------------------------------------------
    //  Priority over row clicks
    // --------------------------------------------------------

    [Fact]
    public void PressingTheBarDoesNotPickTheRowBehindIt()
    {
        var picks = 0;
        var list = List();
        list.Picked += _ => picks++;
        Frame(list);

        list.OnPointerDown(new SKPoint(BarX, 150f));
        list.OnPointerUp(new SKPoint(BarX, 150f));

        Assert.Equal(0, picks);
        Assert.NotEqual(0f, list.ScrollOffset);
    }

    [Fact]
    public void PressingARowStillPicksIt()
    {
        var picks = 0;
        var list = List();
        list.Picked += _ => picks++;
        Frame(list);

        list.OnPointerDown(new SKPoint(10f, 150f));
        list.OnPointerUp(new SKPoint(10f, 150f));

        Assert.Equal(1, picks);
    }

    // --------------------------------------------------------
    //  Priority over children and resize edges, through UIContext
    // --------------------------------------------------------

    [Fact]
    public void TheBarOutranksTheChildDrawnUnderneathIt()
    {
        // VScrollContainer children are not Transient, so the full-width child is the deeper
        // hit and would swallow the press if the bar did not claim the point first.
        using var context = new UIContext();
        var scroller = Scroller();
        context.Root = scroller;
        Frame(context);

        context.HandlePointerDown(new SKPoint(BarX, 150f));

        Assert.NotEqual(0f, scroller.ScrollOffset);
    }

    [Fact]
    public void TheBarOutranksTheResizeEdgeItOverlaps()
    {
        using var context = new UIContext();
        var scroller = Scroller();
        scroller.ResizeEdges = ResizeEdge.Right;
        context.Root = scroller;
        Frame(context);

        context.HandlePointerDown(new SKPoint(BarOverResizeEdgeX, 150f));

        Assert.False(context.IsResizing);
        Assert.Null(scroller.Width);
        Assert.NotEqual(0f, scroller.ScrollOffset);
    }

    [Fact]
    public void TheResizeEdgeStillWorksWhereThereIsNoBar()
    {
        using var context = new UIContext();
        var scroller = Scroller(contentHeight: ViewportHeight / 2f);
        scroller.ResizeEdges = ResizeEdge.Right;
        context.Root = scroller;
        Frame(context);

        context.HandlePointerDown(new SKPoint(BarOverResizeEdgeX, 150f));

        Assert.True(context.IsResizing);
    }

    [Fact]
    public void DraggingSurvivesThePointerLeavingTheComponent()
    {
        // The bar is 7px wide and hugs the edge, so the pointer wandering off sideways during a
        // drag is the normal case, not an edge case. Pointer capture has to keep feeding it moves.
        using var context = new UIContext();
        var scroller = Scroller();
        context.Root = scroller;
        Frame(context);

        context.HandlePointerDown(new SKPoint(BarX, TrackTop));
        context.HandlePointerMove(new SKPoint(-500f, TrackTop + ThumbTravel / 2f));

        AssertClose(MaxScroll / 2f, scroller.ScrollOffset);
    }

    // --------------------------------------------------------
    //  Helpers
    // --------------------------------------------------------

    private static VScrollContainer Scroller(float contentHeight = ContentHeight)
    {
        var scroller = new VScrollContainer
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Expand
        };

        scroller.AddComponent(Spacer(contentHeight));
        return scroller;
    }

    private static ListView<int> List() =>
        new([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], (_, _) => Spacer(ContentHeight / 10f))
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Expand
        };

    private static IComponent Spacer(float height) => new HStack
    {
        HorizontalSize = SizeMode.Expand,
        VerticalSize = SizeMode.Fixed,
        Height = height
    };

    /// <summary>Runs one layout + render pass, which is what publishes the bar's geometry.</summary>
    private static void Frame(IComponent root)
    {
        using var surface = NewSurface();

        root.Measure(new SKSize(ViewportWidth, ViewportHeight));
        root.Resolve();
        root.Arrange(new SKRect(0, 0, ViewportWidth, ViewportHeight));
        root.Render(surface.Canvas);
    }

    private static void Frame(UIContext context)
    {
        Frame(context.Root!);
        context.ClearDirty();
    }

    private static SKSurface NewSurface() =>
        SKSurface.Create(new SKImageInfo((int)ViewportWidth, (int)ViewportHeight));

    private static void AssertClose(float expected, float actual) =>
        Assert.True(Math.Abs(expected - actual) < 0.5f, $"Expected ~{expected}, got {actual}");
}