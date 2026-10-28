using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;
using Lyra.DuplicateStatusProvider;
using Lyra.FileLoader.Navigation;
using Lyra.FileLoader.Store;
using Lyra.GUI;
using Lyra.GUI.Presenters;
using Lyra.Imaging.Content.Status;
using Lyra.Renderer;
using SkiaSharp;

namespace Lyra.SdlCore;

public partial class SdlCore
{
    private UIState CurrentUIState() =>
        UIState.Create(_composite, GetApplicationStates(), DirectoryNavigator.GetSnapshot(), DirectoryNavigator.GetNavigation(), FileRecordDatabase.Current);

    private ApplicationStates GetApplicationStates()
    {
        var navigation = DirectoryNavigator.GetNavigation();
        var drop = _dropProgressTracker.GetDropStatus();
        var scan = _duplicateScanService.Progress.GetScanStatus();

        return new ApplicationStates
        {
            CollectionType = DirectoryNavigator.GetCollectionType(),
            CollectionIndex = navigation.CollectionIndex,
            CollectionCount = navigation.CollectionCount,
            DirectoryIndex = navigation.DirectoryIndex,
            DirectoryCount = navigation.DirectoryCount,
            ViewMode = CurrentViewMode(),
            Zoom = _zoomPercentage,
            DisplayMode = _displayMode,
            ViewLocked = _viewState.ViewLocked,
            SamplingMode = GetSamplingModeDescription(),
            InfoVisible = _viewState.InfoVisible,
            HelpVisible = _viewState.HelpVisible,
            SidebarVisible = _viewState.SidebarVisible,
            DropActive = drop.Active,
            DropAborted = drop.Aborted,
            DropPathsEnqueued = drop.PathsEnqueued,
            DropFilesEnumerated = drop.FilesEnumerated,
            DropFilesSupported = drop.FilesSupported,
            ScanActive = scan.Active,
            ScanAborted = scan.Aborted,
            ScanPhase = scan.Phase,
            ScanDone = scan.Done,
            ScanTotal = scan.Total,
            Backend = _renderer.Backend,
            Display = _displays.Current,
            BackendSupportsExtendedRange = _renderer.BackendSupportsExtendedRange
        };
    }

    private string GetSamplingModeDescription()
    {
        return _composite?.Content?.IsResolutionIndependent == true
            ? "Disabled (resolution-independent)"
            : _viewState.SamplingMode.Description();
    }

    private readonly LoadProgressPresenter _loadProgress = new();
    
    private void UpdateStatusOverlay(in RenderScene scene)
    {
        var composite = scene.Composite;
        var textColor = scene.Background == BackgroundMode.White ? SKColors.Black : SKColors.White;

        var load = LoadSnapshot.Of(composite);
        _ui.SetLoadProgress(_loadProgress.Update(load), textColor);

        if (!_running)
        {
            _ui.SetStatusOverlay("Closing...", textColor);
            return;
        }

        if (_duplicateScanService.Progress.GetScanStatus() is { Active: true } scan)
        {
            _ui.SetStatusOverlay(FormatScanStatus(scan), textColor);
            return;
        }

        var drop = _dropProgressTracker.GetDropStatus();
        if (drop is { Active: true, FilesEnumerated: > 300 })
        {
            _ui.SetStatusOverlay($"{drop.FilesSupported} images found, {drop.FilesEnumerated} files scanned...", textColor);
            return;
        }

        if (composite == null || composite.State == CompositeState.Failed)
        {
            _ui.SetStatusOverlay("No image", textColor);
            return;
        }

        if (composite.State == CompositeState.Loading)
        {
            _ui.SetStatusOverlay(LoadStatusText.For(load), textColor);
            return;
        }

        _ui.SetStatusOverlay(null, default);
    }

    private static string FormatScanStatus(ScanProgress scan) => scan.Phase switch
    {
        ScanPhase.Exact when scan.Total > 0      => $"Finding duplicates… hashing {scan.Done}/{scan.Total}",
        ScanPhase.Exact                          => "Finding duplicates… measuring sizes",
        ScanPhase.Perceptual when scan.Total > 0 => $"Perceptual scan… {scan.Done}/{scan.Total}",
        ScanPhase.Perceptual                     => "Perceptual scan…",
        _                                        => "Finding duplicates…"
    };

    /// <summary>SDL reports pointer coordinates in density units; the UI lays out in content-scale units.</summary>
    private static float PointerScaleOf(PixelSize size) => size.Scale / size.ContentScale;
}
