using Lyra.Common;
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
    private IRenderer _renderer = null!;
    private readonly GpuBackend _backend;
    private bool _running = true;

    private readonly DropStats _dropStats = new();

    // IMPORTANT: Certain window operations (bring-to-front, fullscreen) are
    // unreliable if performed too early, even when confirmed by SDL events.
    // To avoid unstable behavior, these actions are deferred until a few
    // frames have been rendered.
    private bool _coldStartSafe;
    private int _coldStartFramesPending;
    private const int WindowWarmupFrames = 3;
    private readonly List<Action> _deferredUntilWarm = [];

    private Composite? _composite;
    private int _zoomPercentage = 100;
    private DisplayMode _displayMode = DisplayMode.Undefined;

    private const int PreloadDepth = 3;
    private const int CleanupSafeRange = 4;

    public SdlCore(GpuBackend backend = GpuBackend.OpenGL)
    {
        _backend = backend;

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

    private void InitializeWindowAndRenderer()
    {
        const WindowFlags flags = WindowFlags.Resizable | WindowFlags.Maximized | WindowFlags.HighPixelDensity;

        var (w, h) = GetInitialWindowSize();

        if (_backend == GpuBackend.OpenGL)
        {
            _window = CreateWindow("Lyra Viewer (OpenGL)", w, h, flags | WindowFlags.OpenGL);
            _renderer = new SkiaOpenGlRenderer(_window, _dropStats);
        }
        else if (_backend == GpuBackend.Vulkan)
        {
            _window = CreateWindow("Lyra Viewer (Vulkan)", w, h, flags | WindowFlags.Vulkan);
            _renderer = new SkiaVulkanRenderer(_window);
        }
        
        SetWindowMinimumSize(_window, 640, 480);
        SetWindowFocusable(_window, true);
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
            HandleEvents();
            RecalculateDisplayModeIfNecessary();
            _renderer.Render();
            GLSwapWindow(_window);

            if (!_coldStartSafe)
            {
                if (--_coldStartFramesPending <= 0)
                {
                    _coldStartSafe = true;
                    foreach (var action in _deferredUntilWarm)
                        action();

                    _deferredUntilWarm.Clear();
                }
            }
        }
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

        _renderer.Dispose();
        ImageStore.SaveAndDispose();
        _composite?.Dispose();

        if (_window != IntPtr.Zero)
            DestroyWindow(_window);

        Quit();

        Logger.Info("[Core] Dispose finished.");
    }

    public enum GpuBackend
    {
        OpenGL,
        Vulkan
    }
}