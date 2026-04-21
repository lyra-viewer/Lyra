using Lyra.Common.Settings.Enums;

namespace Lyra.Common.Settings;

public readonly record struct UISettings(
    SamplingMode SamplingMode,
    BackgroundMode BackgroundMode,
    bool InfoVisible,
    bool HelpVisible,
    bool SidebarVisible
)
{
    public static readonly UISettings DefaultUiSettings = new(
        SamplingMode: SamplingMode.Cubic,
        BackgroundMode: BackgroundMode.Black,
        InfoVisible: true,
        HelpVisible: true,
        SidebarVisible: true
    );
}