using Lyra.Common.Settings.Enums;

namespace Lyra.Renderer.GUI;

public interface IUIEvents
{
    event Action? OpenFileRequested;
    event Action? OpenDirectoryRequested;
    event Action? FullscreenRequested;
    event Action? QuitRequested;
    event Action<string>? DirectoryPicked;
    event Action<BackgroundMode>? BackgroundModeChanged;
    event Action<SamplingMode>? SamplingModeChanged;
}