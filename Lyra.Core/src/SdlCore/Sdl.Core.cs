using System.Collections.Concurrent;
using Lyra.Common;
using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using Lyra.DropStatusProvider;
using Lyra.FileLoader;
using Lyra.Imaging;
using Lyra.Imaging.Content;
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

    private IntPtr _window;
    private SkiaRendererBase _renderer = null!;
    private bool _running = true;

    private readonly DropProgressTracker _dropProgressTracker = new();

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

    public SdlCore()
    {
        if (!Init(InitFlags.Video))
        {
            LogError(LogCategory.System, $"SDL could not initialize: {GetError()}");
            return;
        }

        ColdStartReset();
        InitializeWindowAndRenderer();
        InitializeInput();
        ImageStore.Initialize();

        // TODO Load from arguments
        // LoadImage();
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

        switch (SettingsManager.AppSettings.Renderer)
        {
            case Backend.OpenGL:
                _window = CreateWindow("Lyra Viewer (OpenGL)", w, h, flags | WindowFlags.OpenGL);
                _renderer = new SkiaOpenGlRenderer(_window, DimensionHelper.GetDrawableSize(_window), _dropProgressTracker);
                break;
            case Backend.Metal:
                _window = CreateWindow("Lyra Viewer (Metal)", w, h, flags | WindowFlags.Metal);
                _renderer = new SkiaMetalRenderer(_window, DimensionHelper.GetDrawableSize(_window), _dropProgressTracker);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        _nextFrameDeadlineNs = GetTicksNS() + TargetFrameNs;
        SetWindowMinimumSize(_window, 640, 480);
        SetWindowFocusable(_window, true);
        RefreshDisplayInfo();
        SetupCursorCallback();
        SetupDirectoryPickerCallback();

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
            _displayMode = DimensionHelper.GetInitialDisplayMode(_window, _composite, out _zoomPercentage);

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
            _displayMode = DimensionHelper.GetInitialDisplayMode(_window, _composite, out _zoomPercentage);
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

    private void SetupDirectoryPickerCallback()
    {
        _renderer.UIManager.DirectoryPicked += OnDirectoryPicked;
    }

    private void OnCompositeProgress(Composite c)
    {
        DispatchToMain(() => _renderer.UIManager.RefreshCurrent());
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
        _renderer.SetComposite(null);
    }

    public void Dispose()
    {
        Logger.Info("[Core] Disposing...");

        if (_composite is not null)
            _composite.ProgressChanged -= OnCompositeProgress;

        var userSettings = _renderer.ExportUiSettings();
        SettingsManager.SaveUiSettings(userSettings);

        _renderer.Dispose();
        ImageStore.SaveAndDispose();
        _composite?.Dispose();

        if (_window != IntPtr.Zero)
            DestroyWindow(_window);

        Quit();

        Logger.Info("[Core] Dispose finished.");
    }
}