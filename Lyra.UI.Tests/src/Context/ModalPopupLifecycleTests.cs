using Lyra.UI;
using Lyra.UI.Components;
using Lyra.UI.Components.Layout;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using Xunit;

namespace Lyra.UI.Tests.Context;

public class ModalPopupLifecycleTests
{
    private static VStack Content() => new()
    {
        HorizontalSize = SizeMode.Fixed,
        VerticalSize = SizeMode.Fixed,
        Width = 50,
        Height = 20
    };

    // --------------------------------------------------------
    //  Popups
    // --------------------------------------------------------

    [Fact]
    public void DismissingAPopupDetachesTheContentWithoutDisposingIt()
    {
        using var context = new UIContext();
        var panel = Content();

        context.ShowPopup(panel, new SKPoint(10, 10));
        Assert.NotNull(panel.Parent);

        context.DismissPopup();

        Assert.Null(panel.Parent);

        // Still usable: the owner can show it again.
        context.ShowPopup(panel, new SKPoint(20, 20));
        Assert.NotNull(panel.Parent);
    }

    [Fact]
    public void APopupDismissCallbackFiresExactlyOnce()
    {
        using var context = new UIContext();
        var dismissals = 0;

        context.ShowPopup(Content(), SKPoint.Empty, () => dismissals++);

        context.DismissPopup();
        context.DismissPopup();
        context.DismissPopup();

        Assert.Equal(1, dismissals);
    }

    [Fact]
    public void ShowingASecondPopupDismissesTheFirst()
    {
        using var context = new UIContext();
        var dismissals = 0;

        context.ShowPopup(Content(), SKPoint.Empty, () => dismissals++);
        context.ShowPopup(Content(), SKPoint.Empty);

        Assert.Equal(1, dismissals);
    }

    [Fact]
    public void DismissingAPopupStopsItBlockingInput()
    {
        using var context = new UIContext();

        context.ShowPopup(Content(), SKPoint.Empty);
        Assert.True(context.GetLayer("Popup")!.BlocksInput);

        context.DismissPopup();

        Assert.False(context.GetLayer("Popup")!.BlocksInput);
        Assert.Null(context.GetLayer("Popup")!.Root);
    }

    // --------------------------------------------------------
    //  Modals
    // --------------------------------------------------------

    [Fact]
    public void DismissingAModalDetachesTheContentWithoutDisposingIt()
    {
        using var context = new UIContext();
        var panel = Content();

        context.ShowModal(panel);
        Assert.NotNull(panel.Parent);
        Assert.True(context.IsModalOpen);

        context.DismissModal();

        Assert.Null(panel.Parent);
        Assert.False(context.IsModalOpen);

        context.ShowModal(panel);
        Assert.True(context.IsModalOpen);
    }

    [Fact]
    public void DismissModalReportsWhetherThereWasAnythingToDismiss()
    {
        using var context = new UIContext();

        Assert.False(context.DismissModal());

        context.ShowModal(Content());

        Assert.True(context.DismissModal());
        Assert.False(context.DismissModal());
    }

    [Fact]
    public void AModalDismissCallbackFiresExactlyOnce()
    {
        using var context = new UIContext();
        var dismissals = 0;

        context.ShowModal(Content(), () => dismissals++);

        context.DismissModal();
        context.DismissModal();

        Assert.Equal(1, dismissals);
    }

    [Fact]
    public void DismissingAModalStopsItBlockingInput()
    {
        using var context = new UIContext();

        context.ShowModal(Content());
        Assert.True(context.GetLayer("Modal")!.BlocksInput);

        context.DismissModal();

        Assert.False(context.GetLayer("Modal")!.BlocksInput);
    }

    [Fact]
    public void ClickingTheScrimDismissesTheModal()
    {
        using var context = new UIContext();
        var root = new VStack { HorizontalSize = SizeMode.Expand, VerticalSize = SizeMode.Expand };
        context.Root = root;

        var panel = Content();
        context.ShowModal(panel);

        // Lay out both layers so the scrim and the centred panel have bounds.
        foreach (var layer in context.Layers)
        {
            layer.Root!.Measure(new SKSize(400, 300));
            layer.Root.Resolve();
            layer.Root.Arrange(new SKRect(0, 0, 400, 300));
        }

        // Top-left corner is scrim, far from the centred panel.
        context.HandlePointerDown(new SKPoint(5, 5));

        Assert.False(context.IsModalOpen);
    }
}
