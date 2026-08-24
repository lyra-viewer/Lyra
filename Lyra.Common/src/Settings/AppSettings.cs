using Lyra.Common.Settings.Enums;

namespace Lyra.Common.Settings;

public readonly record struct AppSettings(
    Backend Renderer,
    WindowState WindowStateOnStart,
    MidMouseButtonFunction MidMouseButtonFunction,
    int InfoTextSize,
    int HelpTextSize,
    bool PreserveUiSettings,
    bool Debug,
    string Theme
)
{
    public static readonly AppSettings DefaultAppSettings = new(
        Renderer: Backend.Auto,
        WindowState.Maximized,
        MidMouseButtonFunction.Pan,
        14,
        12,
        PreserveUiSettings: true,
        Debug: false,
        Theme: ""
    );
}