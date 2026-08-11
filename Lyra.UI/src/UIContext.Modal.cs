using Lyra.UI.Components;

namespace Lyra.UI;

// ============================================================================
//  UIContext modal host - a single centered, scrim-backed dialog layer.
// ----------------------------------------------------------------------------
//  Distinct from the popup host (UIContext.Popups.cs): popups anchor at a
//  point and dim nothing; a modal centers on screen, blocks all input to the
//  layers beneath it (BlocksInput), and dims them with a scrim that the
//  ModalOverlay draws itself.
//
//  The modal content is an external reference - the ModalOverlay does not own
//  it, so the caller keeps ownership and is responsible for disposal. This is
//  what lets a long-lived panel (e.g. the About dialog) be shown and hidden
//  repeatedly without being rebuilt.
// ============================================================================
public partial class UIContext
{
    private const string ModalLayerName = "Modal";

    private Action? _modalDismissCallback;

    public bool IsModalOpen => GetLayer(ModalLayerName)?.Root is not null;

    public void ShowModal(IComponent content, Action? onDismiss = null)
    {
        // Replace any open modal atomically - fire its dismiss callback
        // first so its source can update any toggle state.
        DismissModal();

        var layer = GetLayer(ModalLayerName) ?? AddLayer(ModalLayerName);
        layer.BlocksInput = true;
        layer.Root = new ModalOverlay(content, () => DismissModal());

        _modalDismissCallback = onDismiss;
        Invalidate();
    }

    /// <summary>
    /// Closes the modal if one is open. Returns true if a modal was dismissed,
    /// false if none was open.
    /// </summary>
    public bool DismissModal()
    {
        var layer = GetLayer(ModalLayerName);
        if (layer?.Root is null)
            return false;

        // The ModalOverlay does not own its content, but it did adopt it as a
        // child - disposing the overlay detaches it without touching the
        // content, which the caller still owns.
        var overlay = layer.Root;
        layer.Root = null;
        layer.BlocksInput = false;
        overlay.Dispose();

        var cb = _modalDismissCallback;
        _modalDismissCallback = null;
        cb?.Invoke();

        Invalidate();
        return true;
    }
}
