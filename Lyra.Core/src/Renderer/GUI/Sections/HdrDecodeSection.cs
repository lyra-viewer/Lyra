using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;
using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;

namespace Lyra.Renderer.GUI.Sections;

public sealed class HdrDecodeSection : IUISection, IDisposable
{
    private readonly Collapsible _collapsible;
    private readonly RadioGroup<ToneMapMode> _curve;
    private readonly ValueSlider _exposure;

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

        var body = new VStack
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Shrink,
            Padding = new Padding(0, 4, 0, 6),
            Spacing = 2f
        };

        body.AddComponent(Caption("Tone curve:"));
        body.AddComponent(_curve);
        body.AddComponent(Caption("Exposure (stops):"));
        body.AddComponent(_exposure);

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
        _collapsible.Present = state.Composite?.IsHdrDecoded == true && HdrToneMapShader.IsAvailable;
    }

    public void SetToneMapMode(ToneMapMode mode) => _curve.Selected = mode;

    public void SetExposureStops(int stops) => _exposure.Value = stops;

    public void Dispose()
    {
        _curve.Dispose();
        _exposure.Dispose();
    }
}