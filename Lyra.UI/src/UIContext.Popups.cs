using Lyra.UI.Components;
using SkiaSharp;

namespace Lyra.UI;

public partial class UIContext : IPopupHost
{
    private const string PopupLayerName = "Popup";

    private Action? _popupDismissCallback;

    public bool IsPopupOpen => GetLayer(PopupLayerName)?.Root is not null;

    public void ShowPopup(IComponent content, SKPoint position, Action? onDismiss = null)
    {
        // Replace any open popup atomically - fire its dismiss
        // callback first so its source can update toggle state.
        DismissPopup();

        var layer = GetLayer(PopupLayerName) ?? AddLayer(PopupLayerName);
        layer.BlocksInput = true;
        layer.Root = new Overlay(content, position, DismissPopup);

        _popupDismissCallback = onDismiss;
        Invalidate();
    }

    public void DismissPopup()
    {
        var layer = GetLayer(PopupLayerName);
        if (layer?.Root is null)
            return;

        // The Overlay does not own its content, but it did adopt it as a child -
        // disposing the overlay detaches it without touching the panel, which
        // the source component still owns and will dispose.
        var overlay = layer.Root;

        if (IsWithin(_hoveredComponent, overlay))
        {
            _hoveredComponent!.OnPointerLeave();
            _hoveredComponent = null;
        }

        layer.Root = null;
        layer.BlocksInput = false;
        overlay.Dispose();

        var cb = _popupDismissCallback;
        _popupDismissCallback = null;
        cb?.Invoke();

        Invalidate();
    }

    private static bool IsWithin(IComponent? component, IComponent root)
    {
        for (var c = component; c is not null; c = c.Parent)
        {
            if (ReferenceEquals(c, root))
                return true;
        }

        return false;
    }
}