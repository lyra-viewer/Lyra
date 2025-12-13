using Lyra.Common;
using Lyra.FileLoader;
using Lyra.Imaging;
using Lyra.Imaging.Data;
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

        InitializeWindowAndRenderer();
        InitializeInput();

        // TODO Load from arguments
        // LoadImage();
    }

    private void InitializeWindowAndRenderer()
    {
        const WindowFlags flags = WindowFlags.Resizable | WindowFlags.Maximized | WindowFlags.HighPixelDensity;
        if (_backend == GpuBackend.OpenGL)
        {
            _window = CreateWindow("Lyra Viewer (OpenGL)", 0, 0, flags | WindowFlags.OpenGL);
            _renderer = new SkiaOpenGlRenderer(_window);
        }
        else if (_backend == GpuBackend.Vulkan)
        {
            _window = CreateWindow("Lyra Viewer (Vulkan)", 0, 0, flags | WindowFlags.Vulkan);
            _renderer = new SkiaVulkanRenderer(_window);
        }
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