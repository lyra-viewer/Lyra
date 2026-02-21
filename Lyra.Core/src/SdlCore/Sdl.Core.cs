using System.Collections.Concurrent;
using Lyra.Common;
using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using Lyra.DropStatusProvider;
using Lyra.FileLoader;
using Lyra.Imaging;
using Lyra.Imaging.Content;
using Lyra.Renderer;
using SkiaSharp;
using static SDL3.SDL;

namespace Lyra.SdlCore;

public partial class SdlCore : IDisposable
{
    private IntPtr _window;
    private SkiaRendererBase _renderer = null!;
    private bool _running = true;

    private readonly DropProgressTracker _dropProgressTracker = new();

    // IMPORTANT: Certain window operations (bring-to-front, fullscreen) are
    // unreliable if performed too early, even when confirmed by SDL events.
    // To avoid unstable behavior, these actions are deferred until a few
    // frames have been rendered.
    private bool _coldStartSafe;
    private int _coldStartFramesPending;
    private const int WindowWarmupFrames = 30;
    private readonly List<Action> _deferredUntilWarm = [];
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

    private Composite? _composite;
    private int _zoomPercentage = 100;
    private DisplayMode _displayMode = DisplayMode.Undefined;

    private readonly AppSettings _appSettings;

    private const int PreloadDepth = 3;
    private const int CleanupSafeRange = 4;

    public SdlCore(AppSettings appSettings, UiSettings uiSettings)
    {
        _appSettings = appSettings;

        if (!Init(InitFlags.Video))
        {
            LogError(LogCategory.System, $"SDL could not initialize: {GetError()}");
            return;
        }

        ColdStartReset();
        InitializeWindowAndRenderer(_appSettings.Renderer, _appSettings.WindowStateOnStart, uiSettings);
        InitializeInput();
        ImageStore.Initialize();

        // TODO Load from arguments
        // LoadImage();
    }

    private void InitializeWindowAndRenderer(Backend backend, WindowState windowStateOnStart, UiSettings uiSettings)
    {
        var flags = WindowFlags.Resizable | WindowFlags.HighPixelDensity;

        if (windowStateOnStart != WindowState.Normal)
            flags |= WindowFlags.Maximized;

        var (w, h) = GetInitialWindowSize();

        switch (backend)
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
        
        _renderer.ApplyUserSettings(uiSettings);

        SetWindowMinimumSize(_window, 640, 480);
        SetWindowFocusable(_window, true);
        RefreshDisplayInfo();

        if (windowStateOnStart == WindowState.Fullscreen)
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

    private void LoadImage()
    {
        var keepPaths = DirectoryNavigator.GetRange(CleanupSafeRange);
        ImageStore.Cleanup(keepPaths);

        var currentPath = DirectoryNavigator.GetCurrent();
        if (currentPath == null)
        {
            _composite = null;
            _panHelper = null;
        }
        else
        {
            _composite = ImageStore.GetImage(currentPath);
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

    public void Run()
    {
        while (_running)
        {
            DrainMainThreadQueue();
            HandleEvents();
            RecalculateDisplayModeIfNecessary();
            _renderer.Render();
            GLSwapWindow(_window);

            if (!_coldStartSafe)
            {
                if (--_coldStartFramesPending <= 0)
                {
                    _coldStartSafe = true;
                    // Deferred actions will be flushed by DrainMainThreadQueue once warm.
                }
            }
        }
    }

    private void DrainMainThreadQueue()
    {
        while (_mainThreadQueue.TryDequeue(out var a))
            a();

        if (_coldStartSafe && _deferredUntilWarm.Count > 0)
        {
            foreach (var a in _deferredUntilWarm)
                a();
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

    private void ExitApplication()
    {
        Logger.Info("[Core] Exiting application...");
        _running = false;
        _renderer.SetComposite(null);
    }

    public void Dispose()
    {
        Logger.Info("[Core] Disposing...");

        if (_appSettings.PreserveUiSettings)
        {
            var userSettings = _renderer.ExportUiSettings();
            SettingsManager.SaveUiSettings(userSettings);
        }

        _renderer.Dispose();
        ImageStore.SaveAndDispose();
        _composite?.Dispose();

        if (_window != IntPtr.Zero)
            DestroyWindow(_window);

        Quit();

        Logger.Info("[Core] Dispose finished.");
    }
}