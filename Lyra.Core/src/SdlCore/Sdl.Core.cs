using System.Collections.Concurrent;
using Lyra.Common;
using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;
using Lyra.DropStatusProvider;
using Lyra.FileLoader.Duplicates;
using Lyra.FileLoader.Duplicates.Perceptual;
using Lyra.FileLoader.Navigation;
using Lyra.Imaging;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Renderer;
using Lyra.UI.SupportingTypes;
using SkiaSharp;
using static SDL3.SDL;

namespace Lyra.SdlCore;

public partial class SdlCore : IDisposable
{
    // -------------------------------------------------------------------------
    //  Window / renderer
    // -------------------------------------------------------------------------
    
    private const string AppId = "com.nineveh.LyraViewer";

    private IntPtr _window;
    private SkiaRendererBase _renderer = null!;
    private bool _running = true;

    private readonly DropProgressTracker _dropProgressTracker = new();
    private readonly DuplicateScanService _duplicateScanService = new(new ImagingThumbnailSource());
    private readonly ViewState _viewState = new(SettingsManager.UiSettings);

    // -------------------------------------------------------------------------
    //  Frame pacing
    // -------------------------------------------------------------------------

    // Safety cap for "vsync forced off" situations.
    private const int MaxFps = 240;
    private const ulong NsPerSecond = 1_000_000_000UL;
    private const ulong TargetFrameNs = NsPerSecond / MaxFps;
    private ulong _nextFrameDeadlineNs;

    // -------------------------------------------------------------------------
    //  Cold start
    // -------------------------------------------------------------------------

    // IMPORTANT: Certain window operations (bring-to-front, fullscreen) are
    // unreliable if performed too early, even when confirmed by SDL events.
    // To avoid unstable behavior, these actions are deferred until a few
    // frames have been rendered.
    private bool _coldStartSafe;
    private int _coldStartFramesPending;
    private const int WindowWarmupFrames = 30;
    private readonly List<Action> _deferredUntilWarm = [];

    // -------------------------------------------------------------------------
    //  Image / display state
    // -------------------------------------------------------------------------

    private Composite? _composite;
    private int _zoomPercentage = 100;
    private DisplayMode _displayMode = DisplayMode.Undefined;

    private const int PreloadDepth = 3;
    private const int CleanupSafeRange = 4;

    // -------------------------------------------------------------------------
    //  Cursor
    // -------------------------------------------------------------------------

    private nint _currentCursor;

    // -------------------------------------------------------------------------
    //  Threading
    // -------------------------------------------------------------------------

    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

    // =========================================================================
    //  Constructor
    // =========================================================================

    public SdlCore(string[]? startupArgs = null)
    {
        var appVersion = typeof(SdlCore).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        SetAppMetadata("Lyra Viewer", appVersion, AppId);

        if (!Init(InitFlags.Video))
        {
            LogError(LogCategory.System, $"SDL could not initialize: {GetError()}");
            return;
        }

        ColdStartReset();
        
        BundledFonts.Register();

        InitializeWindowAndRenderer();
        InitializeInput();
        ImageStore.Initialize();
        HdrDecodeSettings.InitializeFromSettings();

        LoadStartupArgs(startupArgs);
    }
    
    private void LoadStartupArgs(string[]? startupArgs)
    {
        if (startupArgs is null || startupArgs.Length == 0)
            return;
        
        var paths = startupArgs
            .Where(a => !string.IsNullOrWhiteSpace(a) && (File.Exists(a) || Directory.Exists(a)))
            .ToList();

        if (paths.Count == 0)
            return;

        Logger.Info($"[Core] Loading {paths.Count} startup path(s) from arguments.");
        IngestPaths(paths);
    }

    // =========================================================================
    //  Initialization
    // =========================================================================

    private void InitializeWindowAndRenderer()
    {
        var flags = WindowFlags.Resizable | WindowFlags.HighPixelDensity;

        if (SettingsManager.AppSettings.WindowStateOnStart != WindowState.Normal)
            flags |= WindowFlags.Maximized;

        var (w, h) = GetInitialWindowSize();

        var backend = SettingsManager.AppSettings.Renderer;
        if (backend == Backend.Metal && !OperatingSystem.IsMacOS())
        {
            Logger.Warning("[Renderer] Metal backend is only supported on macOS; falling back to OpenGL.");
            backend = Backend.OpenGL;
        }

        switch (backend)
        {
            case Backend.OpenGL:
                _window = CreateWindow("Lyra Viewer (OpenGL)", w, h, flags | WindowFlags.OpenGL);
                _renderer = new SkiaOpenGlRenderer(_window, DimensionHelper.GetDrawableSize(_window), _dropProgressTracker, _viewState, _duplicateScanService.Progress);
                break;
            case Backend.Metal:
                _window = CreateWindow("Lyra Viewer (Metal)", w, h, flags | WindowFlags.Metal);
                _renderer = new SkiaMetalRenderer(_window, DimensionHelper.GetDrawableSize(_window), _dropProgressTracker, _viewState, _duplicateScanService.Progress);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        ApplyWindowIcon();

        _nextFrameDeadlineNs = GetTicksNS() + TargetFrameNs;
        SetWindowMinimumSize(_window, 640, 480);
        SetWindowFocusable(_window, true);
        RefreshDisplayInfo();
        SetupCursorCallback();
        SetupUICallbacks();

        if (SettingsManager.AppSettings.WindowStateOnStart == WindowState.Fullscreen)
            SetFullscreen(true);
    }

    private static (int w, int h) GetInitialWindowSize()
    {
        var display = GetPrimaryDisplay();
        if (display != 0 && GetDisplayUsableBounds(display, out var r))
        {
            var w = Math.Max(900, r.W / 2);
            var h = Math.Max(600, r.H / 2);
            return (w, h);
        }

        return (1280, 800);
    }
    
    private void ApplyWindowIcon()
    {
        try
        {
            using var stream = typeof(SdlCore).Assembly.GetManifestResourceStream("LyraViewer.AppIcon.png");
            if (stream is null)
            {
                Logger.Warning("[Core] App icon resource not found; skipping window icon.");
                return;
            }

            using var decoded = SKBitmap.Decode(stream);
            if (decoded is null)
            {
                Logger.Warning("[Core] App icon could not be decoded; skipping window icon.");
                return;
            }
            
            var info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var rgba = new SKBitmap(info);
            using (var image = SKImage.FromBitmap(decoded))
            {
                if (!image.ReadPixels(rgba.PeekPixels(), 0, 0))
                {
                    Logger.Warning("[Core] App icon pixel conversion failed; skipping window icon.");
                    return;
                }
            }

            var surface = CreateSurfaceFrom(rgba.Width, rgba.Height, PixelFormat.ABGR8888, rgba.GetPixels(), rgba.RowBytes);
            if (surface == IntPtr.Zero)
            {
                Logger.Warning($"[Core] SDL_CreateSurfaceFrom failed for window icon: {GetError()}");
                return;
            }
            
            SetWindowIcon(_window, surface);
            DestroySurface(surface);
        }
        catch (Exception ex)
        {
            Logger.Warning($"[Core] Failed to set window icon: {ex.Message}");
        }
    }

    // =========================================================================
    //  Main loop
    // =========================================================================

    public void Run()
    {
        while (_running)
        {
            DrainMainThreadQueue();
            HandleEvents();
            RecalculateDisplayModeIfNecessary();
            _renderer.RefreshUI(_composite);
            _renderer.Render();

            GLSwapWindow(_window);

            // Safety pacing
            var now = GetTicksNS();
            if (now < _nextFrameDeadlineNs)
            {
                DelayNS(_nextFrameDeadlineNs - now);
                _nextFrameDeadlineNs += TargetFrameNs;
            }
            else
            {
                // Running late - resync to avoid drift.
                _nextFrameDeadlineNs = now + TargetFrameNs;
            }

            // Advance cold start countdown
            if (!_coldStartSafe && --_coldStartFramesPending <= 0)
                _coldStartSafe = true;
        }
    }

    private void RecalculateDisplayModeIfNecessary()
    {
        if (_composite == null || _panHelper == null)
            return;

        if (!_composite.IsEmpty && _displayMode == DisplayMode.Undefined)
        {
            _displayMode = DimensionHelper.GetDisplayMode(_window, _composite, _viewState.InitDisplayMode, out _zoomPercentage);

            _renderer.SetDisplayMode(_displayMode);
            _renderer.SetZoom(_zoomPercentage);

            _panHelper.UpdateZoom(_zoomPercentage);
            _panHelper.CurrentOffset = SKPoint.Empty;
            _panHelper.Clamp();
            _renderer.SetOffset(_panHelper.CurrentOffset);
        }
    }

    private void DrainMainThreadQueue()
    {
        while (_mainThreadQueue.TryDequeue(out var action))
            action();

        if (_coldStartSafe && _deferredUntilWarm.Count > 0)
        {
            foreach (var action in _deferredUntilWarm)
                action();
            _deferredUntilWarm.Clear();
        }
    }

    private void DispatchToMain(Action action, bool requireWarm = false)
    {
        _mainThreadQueue.Enqueue(() =>
        {
            if (requireWarm && !_coldStartSafe)
            {
                _deferredUntilWarm.Add(action);
                return;
            }

            action();
        });
    }

    private void DeferUntilWarm(Action action)
    {
        if (_coldStartSafe)
            action();
        else
            _deferredUntilWarm.Add(action);
    }

    private void ColdStartReset()
    {
        _coldStartFramesPending = WindowWarmupFrames;
        _coldStartSafe = false;
    }

    // =========================================================================
    //  Image loading
    // =========================================================================

    private void LoadImage(NavigationDirection direction = NavigationDirection.None)
    {
        PurgeNotExistingFiles(direction);

        var keepPaths = DirectoryNavigator.GetRange(CleanupSafeRange);
        ImageStore.Cleanup(keepPaths);

        // Detach from the outgoing composite before it's replaced.
        if (_composite is not null)
            _composite.ProgressChanged -= OnCompositeProgress;

        var currentPath = DirectoryNavigator.GetCurrent();
        if (currentPath == null)
        {
            _composite = null;
            _panHelper = null;
        }
        else
        {
            _composite = ImageStore.GetImage(currentPath);
            _composite.ProgressChanged += OnCompositeProgress;

            var preloadPaths = DirectoryNavigator.GetRange(PreloadDepth);
            ImageStore.Preload(preloadPaths);
            _displayMode = DimensionHelper.GetDisplayMode(_window, _composite, _viewState.InitDisplayMode, out _zoomPercentage);
            _panHelper = new PanHelper(_window, _composite, _zoomPercentage);
        }

        _renderer.SetComposite(_composite);
        _renderer.SetOffset(SKPoint.Empty);
        _renderer.SetDisplayMode(_displayMode);
        _renderer.SetZoom(_zoomPercentage);
    }

    private void PurgeNotExistingFiles(NavigationDirection direction = NavigationDirection.None)
    {
        while (DirectoryNavigator.GetCurrent() is { } candidate && !File.Exists(candidate))
        {
            DirectoryNavigator.Purge(candidate);
            ImageStore.Purge(candidate);

            if (direction == NavigationDirection.Backward && DirectoryNavigator.HasPrevious())
                DirectoryNavigator.MoveToPrevious();
        }
    }

    // =========================================================================
    //  Callbacks & event handlers
    // =========================================================================

    private void SetupCursorCallback()
    {
        _renderer.UIManager.SetCursorCallback(cursor =>
        {
            var sdlCursor = cursor switch
            {
                CursorType.ResizeEW => CreateSystemCursor(SystemCursor.EWResize),
                CursorType.ResizeNS => CreateSystemCursor(SystemCursor.NSResize),
                CursorType.ResizeNWSE => CreateSystemCursor(SystemCursor.NWSEResize),
                CursorType.ResizeNESW => CreateSystemCursor(SystemCursor.NESWResize),
                _ => CreateSystemCursor(SystemCursor.Default)
            };

            SetCursor(sdlCursor);

            if (_currentCursor != nint.Zero)
                DestroyCursor(_currentCursor);

            _currentCursor = sdlCursor;
        });
    }

    private void SetupUICallbacks()
    {
        var events = _renderer.UIManager.Events;

        // User-driven UI events (menu clicks, tree picks, dropdown changes)
        events.OpenFileRequested              += OnOpenFileRequested;
        events.OpenDirectoryRequested         += OnOpenDirectoryRequested;
        events.QuitRequested                  += ExitApplication;
        events.AboutRequested                 += OnAboutRequested;
        events.FullscreenRequested            += ToggleFullscreen;
        events.FindDuplicatesRequested        += OnFindDuplicatesRequested;
        events.DuplicatesGoBackRequested      += OnDuplicatesGoBack;
        events.DuplicatesExactOnlyChanged     += OnDuplicatesExactOnlyChanged;
        events.DuplicatesHashToleranceChanged += OnDuplicatesHashToleranceChanged;
        events.DirectoryPicked                += OnDirectoryPicked;
        events.VariantSelected                += OnVariantSelected;

        _duplicateScanService.Completed       += OnDuplicateScanCompleted;
        _duplicateScanService.Aborted         += OnDuplicateScanAborted;

        // Dropdown changes route into ViewState (single source of truth).
        events.BackgroundModeChanged          += _viewState.SetBackgroundMode;
        events.SamplingModeChanged            += _viewState.SetSamplingMode;
        events.ToneMapModeChanged             += _viewState.SetToneMapMode;
        events.ExposureStopsChanged           += _viewState.SetExposureStops;
        events.InitDisplayModeChanged         += _viewState.SetInitDisplayMode;

        // ViewState changes auto-sync the dropdowns. UIManager.Set* is
        // documented as not re-firing, and ViewState's equality guard
        // is a second line of defense against feedback loops.
        _viewState.BackgroundModeChanged      += _renderer.UIManager.SetBackgroundMode;
        _viewState.SamplingModeChanged        += _renderer.UIManager.SetSamplingMode;
        _viewState.ToneMapModeChanged         += _renderer.UIManager.SetToneMapMode;
        _viewState.ToneMapModeChanged         += OnToneMapModeChanged;
        _viewState.ExposureStopsChanged       += _renderer.UIManager.SetExposureStops;
        _viewState.ExposureStopsChanged       += OnExposureStopsChanged;
        _viewState.InitDisplayModeChanged     += _renderer.UIManager.SetInitDisplayMode;
    }

    private void OnCompositeProgress(Composite c)
    {
        DispatchToMain(() => _renderer.UIManager.RefreshCurrent());
    }
    
    private void OnToneMapModeChanged(ToneMapMode mode)
    {
        HdrDecodeSettings.ToneMapMode = mode;
        _renderer.UIManager.Invalidate();
    }

    private void OnExposureStopsChanged(int stops)
    {
        HdrDecodeSettings.ExposureStops = stops;
        _renderer.UIManager.Invalidate();
    }

    private void OnVariantSelected(int index)
    {
        if (_composite?.Content is not VariantRasterContent variants || !variants.Select(index))
            return;

        _displayMode = DimensionHelper.GetDisplayMode(_window, _composite, _viewState.InitDisplayMode, out _zoomPercentage);
        _panHelper = new PanHelper(_window, _composite, _zoomPercentage);

        _renderer.SetComposite(_composite);
        _renderer.SetOffset(SKPoint.Empty);
        _renderer.SetDisplayMode(_displayMode);
        _renderer.SetZoom(_zoomPercentage);

        _renderer.UIManager.RefreshCurrent();
    }
    
    private void OnAboutRequested()
    {
        if (OperatingSystem.IsMacOS())
            SystemUtils.MacAboutPanel.Show();
        else
            _renderer.UIManager.ShowAboutModal();
    }

    private void OnDirectoryPicked(string absoluteDir)
    {
        var currentDir = DirectoryNavigator.GetCurrentDirectory();
        if (string.Equals(currentDir, absoluteDir, StringComparison.Ordinal))
            return; // already in this directory

        if (DirectoryNavigator.MoveToFirstInDirectory(absoluteDir))
            LoadImage();
    }

    // =========================================================================
    //  Lifecycle
    // =========================================================================

    private void ExitApplication()
    {
        Logger.Info("[Core] Exiting application...");
        _running = false;
        _duplicateScanService.Shutdown();
        _renderer.SetComposite(null);
    }

    public void Dispose()
    {
        Logger.Info("[Core] Disposing...");

        if (_composite is not null)
            _composite.ProgressChanged -= OnCompositeProgress;

        SettingsManager.SaveUiSettings(_viewState.Export());

        _renderer.Dispose();
        ImageStore.SaveAndDispose();
        _composite?.Dispose();

        if (_window != IntPtr.Zero)
            DestroyWindow(_window);

        Quit();

        Logger.Info("[Core] Dispose finished.");
    }
}