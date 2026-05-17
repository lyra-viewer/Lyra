using Lyra.Common.Settings.Enums;
using Lyra.Renderer.GUI;
using Lyra.Renderer.GUI.Layers;
using Lyra.UI;
using Lyra.UI.SupportingTypes;
using SkiaSharp;

namespace Lyra.Renderer;

// ============================================================================
//  UIManager - orchestrates layers and routes SDL input to UIContext.
// ----------------------------------------------------------------------------
//  Responsibilities:
//    - Own the UIContext and its layers.
//    - Forward SDL input events to UIContext.
//    - Delegate layer-specific logic to layer classes.
//
//  Layer structure (bottom to top):
//    - StatusLayer  - centered status text (No image, Loading...)
//    - MainLayer    - primary UI tree (info pane, sidebar, sections)
//    - PopupLayer   - dropdowns, context menus  (BlocksInput, lazy)
//    - (future) ModalLayer  - settings, dialogs        (BlocksInput + BlocksVisual)
// ============================================================================
public class UIManager : IDisposable
{
    private readonly UIContext _context = new();

    private readonly StatusLayer _statusLayer;
    private readonly MainLayer _mainLayer;

    /// <summary>
    /// User-driven UI events (menu clicks, tree picks, dropdown changes).
    /// </summary>
    public IUIEvents Events => _mainLayer;

    public float DisplayScale { get; set; }

    public UIManager(float displayScale)
    {
        DisplayScale = displayScale;

        // Layers are added in bottom-to-top order.
        _statusLayer = new StatusLayer(_context);
        _mainLayer = new MainLayer(_context);
    }

    // --------------------------------------------------------
    //  Status overlay
    // --------------------------------------------------------

    /// <summary>
    /// Sets the centered status text (e.g. "No image", "Loading...").
    /// Pass null to hide.  Decision logic lives in the caller
    /// (SkiaRendererBase); this just forwards to StatusLayer.
    /// </summary>
    public void SetStatusOverlay(string? text, SKColor textColor)
    {
        _statusLayer.SetStatus(text, textColor);
        Invalidate();
    }

    // --------------------------------------------------------
    //  State refresh
    // --------------------------------------------------------

    public void Refresh(UIState state) => _mainLayer.Refresh(state);

    public void RefreshCurrent() => _mainLayer.RefreshCurrent();

    // --------------------------------------------------------
    //  Programmatic dropdown sync (driven by keyboard toggles)
    // --------------------------------------------------------

    /// <summary>
    /// Updates the background dropdown's selected value to match the
    /// renderer state. Does not fire <see cref="IUIEvents.BackgroundModeChanged"/>.
    /// </summary>
    public void SetBackgroundMode(BackgroundMode mode)
    {
        _mainLayer.SetBackgroundMode(mode);
        Invalidate();
    }

    /// <summary>
    /// Updates the sampling dropdown's selected value to match the
    /// renderer state. Does not fire <see cref="IUIEvents.SamplingModeChanged"/>.
    /// </summary>
    public void SetSamplingMode(SamplingMode mode)
    {
        _mainLayer.SetSamplingMode(mode);
        Invalidate();
    }

    // --------------------------------------------------------
    //  Render
    // --------------------------------------------------------

    public void Render(SKCanvas canvas) => LyraUI.Process(_context, canvas);

    public void Invalidate() => _context.Invalidate();

    // --------------------------------------------------------
    //  Input
    // --------------------------------------------------------
    //  SDL3 reports mouse coordinates in points (logical), not physical
    //  pixels. The UI layout operates in logical space (canvas is
    //  pre-scaled by DisplayScale), so SDL coordinates can be passed
    //  through directly.
    // --------------------------------------------------------

    public bool HandlePointerDown(float x, float y)
    {
        var consumed = _context.HandlePointerDown(new SKPoint(x, y));
        _context.Invalidate();
        return consumed;
    }

    public bool HandlePointerUp(float x, float y)
    {
        var consumed = _context.HandlePointerUp(new SKPoint(x, y));
        _context.Invalidate();
        return consumed;
    }

    public void HandlePointerMove(float x, float y)
    {
        _mainLayer.SetDebugPointer(x, y);
        _context.HandlePointerMove(new SKPoint(x, y));
        _mainLayer.SetDebugHover(_context.HoveredComponent);
        _context.Invalidate();
    }

    public bool HandleScroll(float x, float y, float deltaX, float deltaY)
    {
        var consumed = _context.HandleScroll(new SKPoint(x, y), deltaX, deltaY);

        if (consumed)
            _context.Invalidate();

        return consumed;
    }

    public void SetCursorCallback(Action<CursorType> callback) => _context.SetCursorCallback(callback);

    // --------------------------------------------------------
    //  Dispose
    // --------------------------------------------------------

    public void Dispose()
    {
        _mainLayer.Dispose();
        _statusLayer.Dispose();
        _context.Dispose();
    }
}