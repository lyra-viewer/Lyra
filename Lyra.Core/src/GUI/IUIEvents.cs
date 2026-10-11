using Lyra.Common.Settings.Enums;

namespace Lyra.GUI;

public interface IUIEvents
{
    event Action? OpenFileRequested;
    event Action? OpenDirectoryRequested;
    event Action? FullscreenRequested;
    event Action? QuitRequested;
    event Action? AboutRequested;
    event Action? InfoToggleRequested;
    event Action? SidebarToggleRequested;
    event Action? HelpToggleRequested;
    event Action? ViewLockToggleRequested;
    event Action? FindDuplicatesRequested;
    event Action<bool>? DuplicatesExactOnlyChanged;
    event Action<int>? DuplicatesHashToleranceChanged;
    event Action? DuplicatesGoBackRequested;
    event Action<string>? DirectoryPicked;
    event Action<InitDisplayMode>? InitDisplayModeChanged;
    event Action<BackgroundMode>? BackgroundModeChanged;
    event Action<SamplingMode>? SamplingModeChanged;
    event Action<ToneMapMode>? ToneMapModeChanged;
    event Action<int>? ExposureStopsChanged;
    event Action<int>? VariantSelected;
}