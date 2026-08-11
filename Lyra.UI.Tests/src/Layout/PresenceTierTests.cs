using Lyra.UI.Components;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Layout;

/// <summary>
/// Present, Visible and Enabled look interchangeable and are not. Each drops out at a
/// different stage, and picking the wrong one produces a bug that reads as a layout problem:
///
///   Present = false  - excluded from layout entirely; siblings close the gap
///   Visible = false  - measured and arranged, but nothing is drawn; the gap stays
///   Enabled = false  - drawn (dimmed) and laid out, but takes no input
///
/// Rendering is checked against real pixels rather than a flag, since "not drawn" is the
/// whole claim.
/// </summary>
public class PresenceTierTests
{
    private const int Size = 100;
    private static readonly SKColor Fill = new(255, 0, 0);
    private static readonly SKColor Cleared = new(0, 0, 255);

    private static VStack Block(float height) => new()
    {
        HorizontalSize = SizeMode.Fixed,
        VerticalSize = SizeMode.Fixed,
        Width = 40,
        Height = height,
        BackgroundColor = Fill
    };

    private static SKColor RenderAndSample(IComponent root, int x, int y)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Size, Size));
        surface.Canvas.Clear(Cleared);

        root.Measure(new SKSize(Size, Size));
        root.Resolve();
        root.Arrange(new SKRect(0, 0, Size, Size));
        root.Render(surface.Canvas);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
    }

    // --------------------------------------------------------
    //  Layout participation
    // --------------------------------------------------------

    [Fact]
    public void PresentFalseRemovesTheComponentFromLayout()
    {
        var hidden = Block(30);
        var sibling = Block(20);

        var stack = new VStack();
        stack.AddComponents(hidden, sibling);
        stack.Measure(new SKSize(Size, Size));

        Assert.Equal(50f, stack.DesiredSize.Height);

        hidden.Present = false;
        stack.Measure(new SKSize(Size, Size));

        // The gap closes - only the sibling remains.
        Assert.Equal(20f, stack.DesiredSize.Height);
    }

    [Fact]
    public void VisibleFalseKeepsTheComponentInLayout()
    {
        var invisible = Block(30);
        var sibling = Block(20);

        var stack = new VStack();
        stack.AddComponents(invisible, sibling);

        invisible.Visible = false;
        stack.Measure(new SKSize(Size, Size));
        stack.Resolve();
        stack.Arrange(new SKRect(0, 0, Size, Size));

        // Space is still reserved, and the sibling is still pushed down by it.
        Assert.Equal(50f, stack.DesiredSize.Height);
        Assert.Equal(30f, invisible.ArrangedBounds.Height);
        Assert.Equal(30f, sibling.ArrangedBounds.Top);
    }

    [Fact]
    public void EnabledFalseKeepsTheComponentInLayout()
    {
        var disabled = Block(30);
        var stack = new VStack();
        stack.AddComponent(disabled);

        disabled.Enabled = false;
        stack.Measure(new SKSize(Size, Size));

        Assert.Equal(30f, stack.DesiredSize.Height);
    }

    // --------------------------------------------------------
    //  Rendering
    // --------------------------------------------------------

    [Fact]
    public void AVisibleComponentIsDrawn()
    {
        Assert.Equal(Fill, RenderAndSample(Block(30), 10, 10));
    }

    [Fact]
    public void AnInvisibleComponentIsNotDrawn()
    {
        var block = Block(30);
        block.Visible = false;

        Assert.Equal(Cleared, RenderAndSample(block, 10, 10));
    }

    [Fact]
    public void ADisabledComponentIsStillDrawn()
    {
        var block = Block(30);
        block.Enabled = false;

        // Disabled dims content, but the component is not hidden.
        Assert.NotEqual(Cleared, RenderAndSample(block, 10, 10));
    }

    [Fact]
    public void AnInvisibleAncestorHidesItsChildren()
    {
        var child = Block(30);
        var parent = new VStack { BackgroundColor = null };
        parent.AddComponent(child);
        parent.Visible = false;

        Assert.Equal(Cleared, RenderAndSample(parent, 10, 10));
    }

    // --------------------------------------------------------
    //  Cascade
    // --------------------------------------------------------

    [Fact]
    public void EffectiveVisibilityFollowsTheParentChain()
    {
        var child = Block(10);
        var parent = new VStack();
        parent.AddComponent(child);

        Assert.True(child.IsEffectivelyVisible);

        parent.Visible = false;
        Assert.False(child.IsEffectivelyVisible);
        Assert.True(child.Visible); // its own flag is untouched

        parent.Visible = true;
        Assert.True(child.IsEffectivelyVisible);
    }

    [Fact]
    public void EffectiveEnablementFollowsTheParentChain()
    {
        var child = Block(10);
        var middle = new VStack();
        var root = new VStack();

        middle.AddComponent(child);
        root.AddComponent(middle);

        Assert.True(child.IsEffectivelyEnabled);

        root.Enabled = false;
        Assert.False(child.IsEffectivelyEnabled);
        Assert.False(middle.IsEffectivelyEnabled);
        Assert.True(child.Enabled);
    }
}
