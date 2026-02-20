using Lyra.Common.Settings.Enums;

namespace Lyra.Common.Settings;

public readonly record struct AppSettings(
    Backend Renderer,
    WindowState WindowStateOnStart,
    bool PreserveUiSettings
)
{
    public static readonly AppSettings DefaultAppSettings = new(
        Renderer: Backend.OpenGL,
        WindowState.Maximized,
        PreserveUiSettings: true
    );
}