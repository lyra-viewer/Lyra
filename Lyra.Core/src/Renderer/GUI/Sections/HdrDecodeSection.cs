using Lyra.Common.Settings.Enums;
using Lyra.Imaging.Content;
using Lyra.Common.Settings;
using Lyra.Common.SystemExtensions;
using Lyra.Renderer.Drawing;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.Components;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;

namespace Lyra.Renderer.GUI.Sections;

public sealed class HdrDecodeSection : IUISection, IDisposable
{
    private readonly Collapsible _collapsible;
    private readonly RadioGroup<ToneMapMode> _curve;
    private readonly ValueSlider _exposure;
    
    private readonly VStack _controls;
    private readonly VStack _note;
    private readonly Label _noteHeadline;
    private readonly Label _noteReason;

    public Collapsible Collapsible => _collapsible;

    public IComponent Root => _collapsible;

    public event Action<ToneMapMode>? ToneMapModeChanged;
    public event Action<int>? ExposureStopsChanged;

    public HdrDecodeSection()
    {
        _curve = new RadioGroup<ToneMapMode>(Enum.GetValues<ToneMapMode>(), mode => mode.Description(), SettingsManager.UiSettings.ToneMapMode);
        _curve.SelectionChanged += mode => ToneMapModeChanged?.Invoke(mode);

        _exposure = new ValueSlider(SettingsManager.MinExposureStops, SettingsManager.MaxExposureStops, SettingsManager.UiSettings.ExposureStops);
        _exposure.ValueChanged += stops => ExposureStopsChanged?.Invoke(stops);

        _controls = new VStack
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Shrink,
            Spacing = 2f
        };

        _controls.AddComponent(Caption("Tone curve:"));
        _controls.AddComponent(_curve);
        _controls.AddComponent(Caption("Exposure (stops):"));
        _controls.AddComponent(_exposure);

        _noteHeadline = Caption(string.Empty);
        _noteReason = Caption(string.Empty);

        _note = new VStack
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Shrink,
            Spacing = 2f,
            Present = false
        };

        _note.AddComponent(_noteHeadline);
        _note.AddComponent(_noteReason);

        var body = new VStack
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Shrink,
            Padding = new Padding(0, 4, 0, 6),
            Spacing = 2f
        };

        body.AddComponent(_controls);
        body.AddComponent(_note);

        _collapsible = new Collapsible("HDR DECODE")
            {
                HorizontalSize = SizeMode.Expand,
                Present = false
            }
            .Child(body);
    }

    private static Label Caption(string text) =>
        new(text)
        {
            Color = Palette.Muted,
            Transient = true,
            Padding = new Padding(6, 6, 0, 2),
            FontSize = 11
        };
    
    public void Refresh(UIState state)
    {
        var composite = state.Composite;

        _collapsible.Present = composite?.IsHdrImage == true;

        if (!_collapsible.Present)
            return;

        var live = composite!.IsHdrDecoded && HdrToneMapShader.IsAvailable;

        _controls.Present = live;
        _note.Present = !live;

        if (live)
            return;

        var (headline, reason) = Explain(composite);
        _noteHeadline.Text = headline;
        _noteReason.Text = reason;
    }

    internal static (string Headline, string Reason) Explain(Composite composite)
    {
        if (composite.IsHdrDecoded)
            return ("Tone mapping unavailable here.", "The runtime effect did not compile.");

        return ("Tone curve baked in at decode.", composite.HdrBakedReason ?? "Live controls do not apply.");
    }

    internal bool ControlsPresent => _controls.Present;

    internal bool NotePresent => _note.Present;

    public void SetToneMapMode(ToneMapMode mode) => _curve.Selected = mode;

    public void SetExposureStops(int stops) => _exposure.Value = stops;

    public void Dispose()
    {
        _curve.Dispose();
        _exposure.Dispose();
    }
}