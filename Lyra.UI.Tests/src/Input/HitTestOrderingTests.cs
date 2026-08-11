using Lyra.UI;
using Lyra.UI.Components;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Input;

/// <summary>
/// Hit-testing decides which component owns a click, and every rule it applies is invisible
/// until it is wrong: a missed Transient check makes a Button's own label swallow the press,
/// a missed depth rule hands child clicks to the panel behind them, and a missed BlocksInput
/// check lets a click pass through an open menu into the UI underneath.
///
/// Exercised through UIContext rather than the internal traversal, so these pin the dispatch
/// the application actually sees.
/// </summary>
public class HitTestOrderingTests
{
    private const float Width = 400f;
    private const float Height = 300f;

    /// <summary>A leaf that records the presses it receives.</summary>
    private sealed class Target : ComponentBase
    {
        public int Presses { get; private set; }

        protected override SKSize MeasureContent(SKSize availableSize) => new(100, 50);
        protected override void ArrangeContent(SKRect contentBounds) { }
        protected override void RenderContent(SKCanvas canvas, SKRect contentBounds) { }
        protected override void OnPointerDownCore(SKPoint point) => Presses++;
    }

    private static void Layout(UIContext context)
    {
        foreach (var layer in context.Layers)
        {
            if (layer.Root is null || !layer.Root.Present)
                continue;

            layer.Root.Measure(new SKSize(Width, Height));
            layer.Root.Resolve();
            layer.Root.Arrange(new SKRect(0, 0, Width, Height));
        }
    }

    private static VStack Panel() => new()
    {
        HorizontalSize = SizeMode.Expand,
        VerticalSize = SizeMode.Expand
    };

    // --------------------------------------------------------
    //  Depth
    // --------------------------------------------------------

    [Fact]
    public void TheDeepestComponentUnderThePointWins()
    {
        using var context = new UIContext();

        var leaf = new Target();
        var middle = Panel();
        var root = Panel();

        middle.AddComponent(leaf);
        root.AddComponent(middle);
        context.Root = root;
        Layout(context);

        context.HandlePointerDown(new SKPoint(10, 10));

        Assert.Equal(1, leaf.Presses);
    }

    [Fact]
    public void ATransientComponentYieldsToWhatIsBehindIt()
    {
        // This is how a Button's internal Label stays out of the way.
        using var context = new UIContext();

        var passthrough = new Target { Transient = true };
        var owner = Panel();
        var ownerPresses = 0;
        owner.PointerDown = _ => ownerPresses++;

        owner.AddComponent(passthrough);
        context.Root = owner;
        Layout(context);

        context.HandlePointerDown(new SKPoint(10, 10));

        Assert.Equal(0, passthrough.Presses);
        Assert.Equal(1, ownerPresses);
    }

    [Fact]
    public void ATransientContainerStillExposesItsChildren()
    {
        // Transient means "not me", not "nothing inside me".
        using var context = new UIContext();

        var leaf = new Target();
        var transientContainer = Panel();
        transientContainer.Transient = true;
        transientContainer.AddComponent(leaf);

        context.Root = transientContainer;
        Layout(context);

        context.HandlePointerDown(new SKPoint(10, 10));

        Assert.Equal(1, leaf.Presses);
    }

    // --------------------------------------------------------
    //  Exclusions
    // --------------------------------------------------------

    [Fact]
    public void APointOutsideEveryComponentHitsNothing()
    {
        using var context = new UIContext();
        var leaf = new Target();
        var root = Panel();
        root.AddComponent(leaf);
        context.Root = root;
        Layout(context);

        Assert.False(context.HandlePointerDown(new SKPoint(Width + 50f, Height + 50f)));
        Assert.Equal(0, leaf.Presses);
    }

    [Theory]
    [InlineData("present")]
    [InlineData("visible")]
    [InlineData("enabled")]
    public void AnExcludedComponentIsNotHit(string flag)
    {
        using var context = new UIContext();

        var leaf = new Target();
        var root = Panel();
        root.AddComponent(leaf);
        context.Root = root;
        Layout(context);

        switch (flag)
        {
            case "present": leaf.Present = false; break;
            case "visible": leaf.Visible = false; break;
            case "enabled": leaf.Enabled = false; break;
        }

        context.HandlePointerDown(new SKPoint(10, 10));

        Assert.Equal(0, leaf.Presses);
    }

    [Theory]
    [InlineData("visible")]
    [InlineData("enabled")]
    public void AnExclusionOnAnAncestorAlsoExcludesTheChild(string flag)
    {
        using var context = new UIContext();

        var leaf = new Target();
        var middle = Panel();
        middle.AddComponent(leaf);

        var root = Panel();
        root.AddComponent(middle);
        context.Root = root;
        Layout(context);

        switch (flag)
        {
            case "visible": middle.Visible = false; break;
            case "enabled": middle.Enabled = false; break;
        }

        context.HandlePointerDown(new SKPoint(10, 10));

        Assert.Equal(0, leaf.Presses);
    }

    // --------------------------------------------------------
    //  Layers
    // --------------------------------------------------------

    [Fact]
    public void TheTopLayerIsTestedFirst()
    {
        using var context = new UIContext();

        var below = new Target();
        var lower = Panel();
        lower.AddComponent(below);
        context.AddLayer("Lower").Root = lower;

        var above = new Target();
        var upper = Panel();
        upper.AddComponent(above);
        context.AddLayer("Upper").Root = upper;

        Layout(context);

        context.HandlePointerDown(new SKPoint(10, 10));

        Assert.Equal(1, above.Presses);
        Assert.Equal(0, below.Presses);
    }

    [Fact]
    public void ABlockingLayerStopsTheSearchEvenWhenNothingInItIsHit()
    {
        using var context = new UIContext();

        var below = new Target();
        var lower = Panel();
        lower.AddComponent(below);
        context.AddLayer("Lower").Root = lower;

        // An empty blocking layer - the shape of an open popup whose panel is
        // somewhere else on screen.
        var blocker = context.AddLayer("Blocker");
        blocker.Root = new VStack
        {
            HorizontalSize = SizeMode.Fixed,
            VerticalSize = SizeMode.Fixed,
            Width = 1,
            Height = 1
        };
        blocker.BlocksInput = true;

        Layout(context);

        var consumed = context.HandlePointerDown(new SKPoint(200, 200));

        Assert.True(consumed);
        Assert.Equal(0, below.Presses);
    }
}
