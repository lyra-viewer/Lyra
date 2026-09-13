using Lyra.Common.Settings.Enums;

namespace Lyra.Common.Settings;

public readonly record struct UISettings(
    SamplingMode SamplingMode,
    BackgroundMode BackgroundMode,
    InitDisplayMode InitDisplayMode,
    ToneMapMode ToneMapMode,
    int ExposureStops,
    bool InfoVisible,
    bool HelpVisible,
    bool SidebarVisible
)
{
    public static readonly UISettings DefaultUiSettings = new(
        SamplingMode: SamplingMode.Pixel,
        BackgroundMode: BackgroundMode.Black,
        InitDisplayMode: InitDisplayMode.FitLarge,
        ToneMapMode: ToneMapMode.Aces,
        ExposureStops: 0,
        InfoVisible: true,
        HelpVisible: true,
        SidebarVisible: true
    );
}