using Lyra.Common.Settings.Enums;

namespace Lyra.Renderer.GUI;

public interface IUIEvents
{
    event Action? OpenFileRequested;
    event Action? OpenDirectoryRequested;
    event Action? FullscreenRequested;
    event Action? QuitRequested;
    event Action? AboutRequested;
    event Action? FindDuplicatesRequested;
    event Action<bool>? DuplicatesExactOnlyChanged;
    event Action<int>? DuplicatesHashToleranceChanged;
    event Action? DuplicatesGoBackRequested;
    event Action<string>? DirectoryPicked;
    event Action<InitDisplayMode>? InitDisplayModeChanged;
    event Action<BackgroundMode>? BackgroundModeChanged;
    event Action<SamplingMode>? SamplingModeChanged;

    /// <summary>A size was picked in the SIZES dropdown; the payload is the variant index.</summary>
    event Action<int>? VariantSelected;
}