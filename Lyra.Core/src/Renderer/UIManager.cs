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
//    - (future) ModalLayer  - settings, dialogs        (BlocksInput; the
//                                                       ModalOverlay draws its
//                                                       own scrim)
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
    
    public float PointerScale { get; set; } = 1f;

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
            Faint               = Pick(c, "faint", d.Faint),

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
    //  Modal
    // --------------------------------------------------------

    public void ShowAboutModal()
    {
        _mainLayer.ShowAboutModal();
        Invalidate();
    }

    public bool DismissModalIfOpen()
    {
        var dismissed = _context.DismissModal();
        if (dismissed)
            Invalidate();

        return dismissed;
    }
    
    // --------------------------------------------------------
    //  Programmatic dropdown sync (driven by keyboard toggles)
    // --------------------------------------------------------

    /// <summary>
    /// Updates the init-display-mode dropdown's selected value to match the
    /// view state. Does not fire <see cref="IUIEvents.InitDisplayModeChanged"/>.
    /// </summary>
    public void SetInitDisplayMode(InitDisplayMode mode)
    {
        _mainLayer.SetInitDisplayMode(mode);
        Invalidate();
    }

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

    /// <summary>
    /// Updates the tone-map dropdown's selected value. Does not fire
    /// <see cref="IUIEvents.ToneMapModeChanged"/>.
    /// </summary>
    public void SetToneMapMode(ToneMapMode mode)
    {
        _mainLayer.SetToneMapMode(mode);
        Invalidate();
    }

    /// <summary>Updates the exposure slider. Does not fire <see cref="IUIEvents.ExposureStopsChanged"/>.</summary>
    public void SetExposureStops(int stops)
    {
        _mainLayer.SetExposureStops(stops);
        Invalidate();
    }

    /// <summary>Reflects duplicate-scan state on the Duplicates Finder section.</summary>
    public void SetDuplicatesState(bool inDuplicatesMode, bool noDuplicatesFound)
    {
        _mainLayer.SetDuplicatesState(inDuplicatesMode, noDuplicatesFound);
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
    //  SDL reports mouse coordinates in density-logical units. The UI lays
    //  out in content-scale units, so incoming coords are remapped by
    //  PointerScale (1.0 where the two scales match, e.g. macOS).
    // --------------------------------------------------------

    private SKPoint ToUiSpace(float x, float y) => new(x * PointerScale, y * PointerScale);

    public bool HandlePointerDown(float x, float y)
    {
        var consumed = _context.HandlePointerDown(ToUiSpace(x, y));
        _context.Invalidate();
        return consumed;
    }

    public bool HandlePointerUp(float x, float y)
    {
        var consumed = _context.HandlePointerUp(ToUiSpace(x, y));
        _context.Invalidate();
        return consumed;
    }

    public void HandlePointerMove(float x, float y)
    {
        var p = ToUiSpace(x, y);
        _mainLayer.SetDebugPointer(p.X, p.Y);
        _context.HandlePointerMove(p);
        _mainLayer.SetDebugHover(_context.HoveredComponent);
        _context.Invalidate();
    }

    public bool HandleScroll(float x, float y, float delta)
    {
        var p = ToUiSpace(x, y);
        var consumed = _context.HandleScroll(p, delta);

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