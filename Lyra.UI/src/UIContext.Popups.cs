using Lyra.UI.Components;
using SkiaSharp;

namespace Lyra.UI;

public partial class UIContext : IPopupHost
{
    private const string PopupLayerName = "Popup";

    private Action? _popupDismissCallback;

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

        // The Overlay does not own its content, so dropping the
        // reference is enough - the source component still owns
        // and will dispose its panel.
        layer.Root = null;
        layer.BlocksInput = false;

        var cb = _popupDismissCallback;
        _popupDismissCallback = null;
        cb?.Invoke();

        Invalidate();
    }
}