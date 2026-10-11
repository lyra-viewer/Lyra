using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Controls;

public class DropDownMenuTests
{
    private const float Width = 800f;
    private const float Height = 600f;

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

    private static (UIContext Context, DropDownMenu<string> Dropdown) Build(float headerWidth, params string[] options)
    {
        var context = new UIContext();
        var dropdown = new DropDownMenu<string>(context, options, o => o, options[0])
        {
            HorizontalSize = SizeMode.Fixed,
            Width = headerWidth
        };

        context.Root = new VStack { HorizontalSize = SizeMode.Expand, VerticalSize = SizeMode.Expand }.Child(dropdown);
        Layout(context);
        return (context, dropdown);
    }

    private static SKRect OptionsBounds(UIContext context)
    {
        // Overlay -> panel
        var overlay = (IContainer)context.GetLayer("Popup")!.Root!;
        return overlay.Children[0].ArrangedBounds;
    }

    [Fact]
    public void TheOptionsAreAtLeastAsWideAsTheHeader()
    {
        var (context, dropdown) = Build(300f, "A", "B");
        using var _ = context;

        dropdown.Toggle();
        Layout(context);

        Assert.Equal(300f, OptionsBounds(context).Width, 0.5);
    }

    [Fact]
    public void OptionsWiderThanTheHeaderGrowThePanelInsteadOfBeingCut()
    {
        var (context, dropdown) = Build(40f, "A rather long option label");
        using var _ = context;

        dropdown.Toggle();
        Layout(context);

        Assert.True(OptionsBounds(context).Width > 40f);
    }

    [Fact]
    public void AWideListOpenedAtTheRightEdgeStaysInsideTheWindow()
    {
        // The sidebar is docked right, so a list wider than its header used to run off-screen.
        using var context = new UIContext();
        var options = new[] { "A", "Locked (keep zoom & pan) and then some" };
        var dropdown = new DropDownMenu<string>(context, options, o => o, options[0])
        {
            HorizontalSize = SizeMode.Fixed,
            Width = 120f
        };

        context.Root = new HStack { HorizontalSize = SizeMode.Expand, VerticalSize = SizeMode.Expand }
            .Children(new VStack { HorizontalSize = SizeMode.Expand, Transient = true }, dropdown);
        Layout(context);

        dropdown.Toggle();
        Layout(context);

        var bounds = OptionsBounds(context);
        Assert.True(bounds.Width > 120f);
        Assert.True(bounds.Right <= Width, $"panel right edge {bounds.Right} is past the window ({Width})");
    }
}