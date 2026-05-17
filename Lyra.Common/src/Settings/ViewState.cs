using Lyra.Common.Settings.Enums;

namespace Lyra.Common.Settings;

public sealed class ViewState
{
    public SamplingMode SamplingMode { get; private set; }
    public BackgroundMode BackgroundMode { get; private set; }
    public bool InfoVisible { get; private set; }
    public bool HelpVisible { get; private set; }
    public bool SidebarVisible { get; private set; }

    public event Action<SamplingMode>? SamplingModeChanged;
    public event Action<BackgroundMode>? BackgroundModeChanged;

    public ViewState(UISettings initial)
    {
        SamplingMode = initial.SamplingMode;
        BackgroundMode = initial.BackgroundMode;
        InfoVisible = initial.InfoVisible;
        HelpVisible = initial.HelpVisible;
        SidebarVisible = initial.SidebarVisible;
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

    public void ToggleInfo() => InfoVisible = !InfoVisible;
    public void ToggleHelp() => HelpVisible = !HelpVisible;
    public void ToggleSidebar() => SidebarVisible = !SidebarVisible;

    public UISettings Export() =>
        new(SamplingMode, BackgroundMode, InfoVisible, HelpVisible, SidebarVisible);
}