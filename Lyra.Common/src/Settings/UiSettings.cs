using Lyra.Common.Settings.Enums;

namespace Lyra.Common.Settings;

public readonly record struct UiSettings(
    SamplingMode SamplingMode,
    BackgroundMode BackgroundMode,
    InfoMode InfoLevel,
    bool HelpBarVisible
)
{
    public static readonly UiSettings DefaultUiSettings = new(
        SamplingMode: SamplingMode.Cubic,
        BackgroundMode: BackgroundMode.Black,
        InfoLevel: InfoMode.Basic,
        HelpBarVisible: true
    );
}