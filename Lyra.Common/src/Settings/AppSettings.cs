using Lyra.Common.Settings.Enums;

namespace Lyra.Common.Settings;

public readonly record struct AppSettings(
    Backend Renderer,
    WindowState WindowStateOnStart,
    MidMouseButtonFunction MidMouseButtonFunction,
    bool PreserveUiSettings
)
{
    public static readonly AppSettings DefaultAppSettings = new(
        Renderer: Backend.OpenGL,
        WindowState.Maximized,
        MidMouseButtonFunction.Pan,
        PreserveUiSettings: true
    );
}