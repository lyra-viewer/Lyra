using Lyra.Common.Settings.Enums;

namespace Lyra.Common.Settings;

public readonly record struct UISettings(
    SamplingMode SamplingMode,
    BackgroundMode BackgroundMode,
    InitDisplayMode InitDisplayMode,
    bool InfoVisible,
    bool HelpVisible,
    bool SidebarVisible
)
{
    public static readonly UISettings DefaultUiSettings = new(
        SamplingMode: SamplingMode.Pixel,
        BackgroundMode: BackgroundMode.Black,
        InitDisplayMode: InitDisplayMode.FitLarge,
        InfoVisible: true,
        HelpVisible: true,
        SidebarVisible: true
    );
}