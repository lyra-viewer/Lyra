using Lyra.Common.Settings.Enums;

namespace Lyra.Common.Settings;

public sealed class ViewState
{
    public SamplingMode SamplingMode { get; private set; }
    public ToneMapMode ToneMapMode { get; private set; }
    public int ExposureStops { get; private set; }
    public BackgroundMode BackgroundMode { get; private set; }
    public InitDisplayMode InitDisplayMode { get; private set; }
    public bool InfoVisible { get; private set; }
    public bool HelpVisible { get; private set; }
    public bool SidebarVisible { get; private set; }

    public event Action<SamplingMode>? SamplingModeChanged;
    public event Action<ToneMapMode>? ToneMapModeChanged;
    public event Action<int>? ExposureStopsChanged;
    public event Action<BackgroundMode>? BackgroundModeChanged;
    public event Action<InitDisplayMode>? InitDisplayModeChanged;

    public ViewState(UISettings initial)
    {
        SamplingMode = initial.SamplingMode;
        ToneMapMode = initial.ToneMapMode;
        ExposureStops = initial.ExposureStops;
        BackgroundMode = initial.BackgroundMode;
        InitDisplayMode = initial.InitDisplayMode;
        InfoVisible = initial.InfoVisible;
        HelpVisible = initial.HelpVisible;
        SidebarVisible = initial.SidebarVisible;
    }

    public void SetExposureStops(int stops)
    {
        var clamped = Math.Clamp(stops, SettingsManager.MinExposureStops, SettingsManager.MaxExposureStops);
        if (ExposureStops == clamped)
            return;

        ExposureStops = clamped;
        ExposureStopsChanged?.Invoke(clamped);
    }

    public void SetToneMapMode(ToneMapMode mode)
    {
        if (ToneMapMode == mode)
            return;

        ToneMapMode = mode;
        ToneMapModeChanged?.Invoke(mode);
    }

    public void SetSamplingMode(SamplingMode mode)
    {
        if (SamplingMode == mode)
            return;

        SamplingMode = mode;
        SamplingModeChanged?.Invoke(mode);
    }

    public void ToggleSampling() =>
        SetSamplingMode((SamplingMode)(((int)SamplingMode + 1) % Enum.GetValues<SamplingMode>().Length));

    public void SetBackgroundMode(BackgroundMode mode)
    {
        if (BackgroundMode == mode)
            return;

        BackgroundMode = mode;
        BackgroundModeChanged?.Invoke(mode);
    }

    public void ToggleBackground() =>
        SetBackgroundMode((BackgroundMode)(((int)BackgroundMode + 1) % Enum.GetValues<BackgroundMode>().Length));

    public void SetInitDisplayMode(InitDisplayMode mode)
    {
        if (InitDisplayMode == mode)
            return;

        InitDisplayMode = mode;
        InitDisplayModeChanged?.Invoke(mode);
    }

    public void ToggleInfo() => InfoVisible = !InfoVisible;
    public void ToggleHelp() => HelpVisible = !HelpVisible;
    public void ToggleSidebar() => SidebarVisible = !SidebarVisible;

    public UISettings Export() =>
        new(SamplingMode, BackgroundMode, InitDisplayMode, ToneMapMode, ExposureStops, InfoVisible, HelpVisible, SidebarVisible);
}