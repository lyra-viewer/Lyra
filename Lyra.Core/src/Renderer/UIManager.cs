using Lyra.Common;
using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using Lyra.Renderer.GUI;
using Lyra.Renderer.GUI.Layers;
using Lyra.UI;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;
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
        
        LoadConfiguredTheme();

        // Layers are added in bottom-to-top order.
        _statusLayer = new StatusLayer(_context);
        _mainLayer = new MainLayer(_context);
    }

    private static void LoadConfiguredTheme()
    {
        var colors = ThemeLoader.LoadColors(SettingsManager.AppSettings.Theme);
        Palette.Apply(BuildTheme(colors));
    }

    private static Theme BuildTheme(IReadOnlyDictionary<string, Color32> c)
    {
        var d = Palette.Default;
        return new Theme
        {
            Background          = Pick(c, "background", d.Background),

            Foreground          = Pick(c, "foreground", d.Foreground),
            Muted               = Pick(c, "muted", d.Muted),
            Dim                 = Pick(c, "dim", d.Dim),

            SelectedForeground  = Pick(c, "selected_foreground", d.SelectedForeground),
            SelectedBackground  = Pick(c, "selected_background", d.SelectedBackground),
            ForegroundDark      = Pick(c, "foreground_dark", d.ForegroundDark),

            Primary             = Pick(c, "primary", d.Primary),
            HoverPrimary        = Pick(c, "hover_primary", d.HoverPrimary),
            PressedPrimary      = Pick(c, "pressed_primary", d.PressedPrimary),

            Disabled            = Pick(c, "disabled", d.Disabled),

            Accent              = Pick(c, "accent", d.Accent),
            HoverAccent         = Pick(c, "hover_accent", d.HoverAccent),
            PressedAccent       = Pick(c, "pressed_accent", d.PressedAccent),

            Danger              = Pick(c, "danger", d.Danger),
            HoverDanger         = Pick(c, "hover_danger", d.HoverDanger),
            PressedDanger       = Pick(c, "pressed_danger", d.PressedDanger),

            Subtle              = Pick(c, "subtle", d.Subtle),
            HoverSubtle         = Pick(c, "hover_subtle", d.HoverSubtle),
            PressedSubtle       = Pick(c, "pressed_subtle", d.PressedSubtle),

            Border              = Pick(c, "border", d.Border),
            BorderActive        = Pick(c, "border_active", d.BorderActive),

            Panel               = Pick(c, "panel", d.Panel),
        };
    }

    private static SKColor Pick(IReadOnlyDictionary<string, Color32> colors, string key, SKColor fallback)
        => colors.TryGetValue(key, out var c) ? new SKColor(c.R, c.G, c.B, c.A) : fallback;

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