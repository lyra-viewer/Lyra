using Lyra.UI.Components.Controls;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Controls;

public class ContextMenuTests
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

    private static SKPoint Center(MenuItem item) => new(item.ArrangedBounds.MidX, item.ArrangedBounds.MidY);

    private static void Click(UIContext context, SKPoint point)
    {
        context.HandlePointerMove(point);
        context.HandlePointerDown(point);
        context.HandlePointerUp(point);
    }

    [Fact]
    public void PickingAnItemDismissesTheMenuBeforeRunningTheHandler()
    {
        using var context = new UIContext();
        using var menu = new ContextMenu(context);

        bool? openWhenHandled = null;
        var item = menu.AddItem("Info", () => openWhenHandled = context.IsPopupOpen);

        menu.Open(new SKPoint(100, 100));
        Layout(context);
        Click(context, Center(item));

        Assert.False(openWhenHandled);
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void AClickOutsideThePanelsDismisses()
    {
        using var context = new UIContext();
        using var menu = new ContextMenu(context);
        var picked = 0;

        menu.AddItem("Info", () => picked++);
        menu.Open(new SKPoint(100, 100));
        Layout(context);

        Click(context, new SKPoint(700, 500));

        Assert.False(menu.IsOpen);
        Assert.False(context.IsPopupOpen);
        Assert.Equal(0, picked);
    }

    [Fact]
    public void HoveringAHeaderOpensItsSubmenuAndHoveringAPlainRowClosesIt()
    {
        using var context = new UIContext();
        using var menu = new ContextMenu(context);

        var plain = menu.AddItem("Info", () => { });
        var submenu = menu.AddSubmenu("Sampling");
        var picked = 0;
        var choice = submenu.AddItem("Smooth", () => picked++);

        menu.Open(new SKPoint(100, 100));
        Layout(context);

        var header = submenu.Header;
        context.HandlePointerMove(Center(header));
        Layout(context);

        Assert.True(choice.ArrangedBounds.Left >= header.ArrangedBounds.Right - 4f);

        context.HandlePointerMove(Center(plain));
        Layout(context);

        Click(context, Center(choice));

        Assert.Equal(0, picked);
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void PickingASubmenuItemRunsItAndDismissesTheWholeMenu()
    {
        using var context = new UIContext();
        using var menu = new ContextMenu(context);

        var submenu = menu.AddSubmenu("Background");
        var picked = 0;
        var choice = submenu.AddItem("White", () => picked++);

        menu.Open(new SKPoint(100, 100));
        Layout(context);

        var header = submenu.Header;
        Click(context, Center(header));
        Layout(context);
        Click(context, Center(choice));

        Assert.Equal(1, picked);
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void NearTheBottomRightCornerTheMenuStaysInsideTheWindow()
    {
        using var context = new UIContext();
        using var menu = new ContextMenu(context);

        var item = menu.AddItem("A fairly long menu label", () => { }, "[X]");

        menu.Open(new SKPoint(Width - 5, Height - 5));
        Layout(context);

        Assert.True(item.ArrangedBounds.Right <= Width);
        Assert.True(item.ArrangedBounds.Bottom <= Height);
        Assert.True(item.ArrangedBounds.Left >= 0);
        Assert.True(item.ArrangedBounds.Top >= 0);
    }

    [Fact]
    public void ReopeningStartsWithSubmenusClosed()
    {
        using var context = new UIContext();
        using var menu = new ContextMenu(context);

        var submenu = menu.AddSubmenu("Sampling");
        var picked = 0;
        var choice = submenu.AddItem("Pixel", () => picked++);

        menu.Open(new SKPoint(100, 100));
        Layout(context);
        context.HandlePointerMove(Center(submenu.Header));
        Layout(context);
        var where = Center(choice);

        menu.Dismiss();
        menu.Open(new SKPoint(100, 100));
        Layout(context);

        Click(context, where);

        Assert.Equal(0, picked);
    }

    [Fact]
    public void OpeningAgainWhileOpenMovesTheMenu()
    {
        using var context = new UIContext();
        using var menu = new ContextMenu(context);

        var item = menu.AddItem("Info", () => { });

        menu.Open(new SKPoint(100, 100));
        menu.Open(new SKPoint(300, 200));
        Layout(context);

        Assert.True(menu.IsOpen);
        Assert.True(item.ArrangedBounds.Left >= 300);
        Assert.True(item.ArrangedBounds.Top >= 200);
    }
}