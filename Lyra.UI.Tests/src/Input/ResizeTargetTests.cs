using Lyra.UI;
using Lyra.UI.Components;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Input;

/// <summary>
/// Resize-edge detection runs before hit-testing and therefore has to apply the same
/// exclusions. It is easy to miss one: LyraUI.Process skips layers whose root is not
/// Present, so a hidden tree keeps the ArrangedBounds it had when it was last laid out,
/// and an unguarded search will happily find a resize edge in it.
/// </summary>
public class ResizeTargetTests
{
    private const float Width = 400f;
    private const float Height = 300f;

    private static VStack Panel() => new()
    {
        HorizontalSize = SizeMode.Expand,
        VerticalSize = SizeMode.Expand,
        ResizeEdges = ResizeEdge.Right
    };

    private static void Layout(UIContext context)
    {
        foreach (var layer in context.Layers)
        {
            if (layer.Root is null)
                continue;

            layer.Root.Measure(new SKSize(Width, Height));
            layer.Root.Resolve();
            layer.Root.Arrange(new SKRect(0, 0, Width, Height));
        }
    }

    [Fact]
    public void AVisibleResizableRootCanBeDragged()
    {
        using var context = new UIContext();
        context.Root = Panel();
        Layout(context);

        context.HandlePointerDown(new SKPoint(Width - 1f, 150f));

        Assert.True(context.IsResizing);
    }

    [Fact]
    public void ARootThatIsNotPresentCannotBeDragged()
    {
        using var context = new UIContext();
        var panel = Panel();
        context.Root = panel;
        Layout(context);

        // Bounds stay from the last layout - only Present changes.
        panel.Present = false;

        context.HandlePointerDown(new SKPoint(Width - 1f, 150f));

        Assert.False(context.IsResizing);
    }

    [Fact]
    public void ARootThatIsNotVisibleCannotBeDragged()
    {
        using var context = new UIContext();
        var panel = Panel();
        context.Root = panel;
        Layout(context);

        panel.Visible = false;

        context.HandlePointerDown(new SKPoint(Width - 1f, 150f));

        Assert.False(context.IsResizing);
    }

    [Fact]
    public void ADisabledRootCannotBeDragged()
    {
        using var context = new UIContext();
        var panel = Panel();
        context.Root = panel;
        Layout(context);

        panel.Enabled = false;

        context.HandlePointerDown(new SKPoint(Width - 1f, 150f));

        Assert.False(context.IsResizing);
    }

    [Fact]
    public void AChildThatIsNotPresentCannotBeDragged()
    {
        using var context = new UIContext();

        var root = new VStack { HorizontalSize = SizeMode.Expand, VerticalSize = SizeMode.Expand };
        var sidebar = Panel();
        root.AddComponent(sidebar);
        context.Root = root;
        Layout(context);

        sidebar.Present = false;

        context.HandlePointerDown(new SKPoint(Width - 1f, 150f));

        Assert.False(context.IsResizing);
    }
}
