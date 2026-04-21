using Lyra.Common.Events;
using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;
using Lyra.DropStatusProvider;
using Lyra.FileLoader;
using Lyra.Imaging.Content;
using Lyra.Renderer.GUI;
using Lyra.SdlCore;
using SkiaSharp;
using static Lyra.Common.Events.EventManager;

namespace Lyra.Renderer;

public abstract class SkiaRendererBase : IDisposable, IDrawableSizeAware
{
    protected int WindowWidth { get; private set; }
    protected int WindowHeight { get; private set; }
    protected float DisplayScale { get; private set; }

    private Composite? _composite;
    private SKPoint _offset = SKPoint.Empty;
    private DisplayMode _displayMode = DisplayMode.Undefined;
    private int _zoomPercentage = 100;

    private SamplingMode _samplingMode;
    private BackgroundMode _backgroundMode;

    private bool _infoVisible;
    private bool _helpVisible;
    private bool _sidebarVisible;

    private readonly ICompositeContentDrawer _contentDrawer;
    private readonly IDropProgressProvider _dropProgressProvider;

    public UIManager UIManager { get; private set; }

    private readonly string _backend;

    protected SkiaRendererBase(PixelSize drawableSize, IDropProgressProvider dropProgressProvider, string backend)
    {
        _backend = backend;

        WindowWidth = drawableSize.PixelWidth;
        WindowHeight = drawableSize.PixelHeight;
        DisplayScale = drawableSize.Scale;

        _dropProgressProvider = dropProgressProvider;

        Subscribe<DrawableSizeChangedEvent>(OnDrawableSizeChanged);

        _contentDrawer = new SkiaCompositeContentDrawer();

        _samplingMode = SettingsManager.UiSettings.SamplingMode;
        _backgroundMode = SettingsManager.UiSettings.BackgroundMode;
        _infoVisible = SettingsManager.UiSettings.InfoVisible;
        _helpVisible = SettingsManager.UiSettings.HelpVisible;
        _sidebarVisible = SettingsManager.UiSettings.SidebarVisible;

        UIManager = new UIManager(DisplayScale);
    }

    protected abstract SKSurface CreateSurface();

    protected virtual bool DisposeSurfaceAfterRender => true;

    protected virtual void BeforeRender()
    {
    }

    protected virtual void AfterRender(SKSurface surface)
    {
    }

    protected virtual void OnDrawableSizeChangedInternal(int width, int height, float scale)
    {
    }

    public virtual void Render()
    {
        BeforeRender();

        var surface = CreateSurface();
        try
        {
            var canvas = surface.Canvas;

            RenderBackground(canvas);
            RenderComposite(canvas);
            UpdateStatusOverlay();
            RenderUI(canvas);

            AfterRender(surface);
        }
        finally
        {
            if (DisposeSurfaceAfterRender)
                surface.Dispose();
        }
    }

    private void RenderUI(SKCanvas canvas)
    {
        canvas.Save();
        canvas.Scale(DisplayScale);
        UIManager.Render(canvas);
        canvas.Restore();
    }

    public void RefreshUI(Composite? composite)
    {
        UIManager.Refresh(UIState.Create(composite, GetApplicationStates(), DirectoryNavigator.GetSnapshot(), DirectoryNavigator.GetNavigation()));
    }

    private void RenderBackground(SKCanvas canvas)
    {
        switch (_backgroundMode)
        {
            case BackgroundMode.White:
                canvas.Clear(SKColors.White);
                break;
            case BackgroundMode.Checkerboard:
                DrawCheckerboardPattern(canvas);
                break;
            case BackgroundMode.Black:
            default:
                canvas.Clear(SKColors.Black);
                break;
        }
    }

    private void RenderComposite(SKCanvas canvas)
    {
        if (_composite?.Content == null)
            return;

        var sampling = _samplingMode switch
        {
            SamplingMode.Linear => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            SamplingMode.Nearest => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.Nearest),
            SamplingMode.None => SKSamplingOptions.Default,
            SamplingMode.Cubic or _ => new SKSamplingOptions(new SKCubicResampler()),
        };

        var logicalSize = new SKSize(_composite.LogicalWidth, _composite.LogicalHeight);
        var zoomScale = _zoomPercentage / 100f;

        RenderCentered(canvas, logicalSize, c =>
        {
            var destFull = new SKRect(0, 0, logicalSize.Width, logicalSize.Height);

            var visibleFull = ComputeVisibleFullRect(
                imageW: logicalSize.Width,
                imageH: logicalSize.Height,
                windowPxW: WindowWidth,
                windowPxH: WindowHeight,
                displayScale: DisplayScale,
                zoomPercentage: _zoomPercentage,
                offsetPx: _offset
            );

            _contentDrawer.Draw(c, _composite, destFull, visibleFull, sampling, zoomScale, DisplayScale);
        });
    }

    private void RenderCentered(SKCanvas canvas, SKSize logicalSize, Action<SKCanvas> drawContent)
    {
        var zoomScale = _zoomPercentage / 100f;

        var drawWidth = logicalSize.Width * zoomScale;
        var drawHeight = logicalSize.Height * zoomScale;

        var logicalWindowWidth = WindowWidth / DisplayScale;
        var logicalWindowHeight = WindowHeight / DisplayScale;

        var left = (logicalWindowWidth - drawWidth) / 2 + _offset.X / DisplayScale;
        var top = (logicalWindowHeight - drawHeight) / 2 + _offset.Y / DisplayScale;

        canvas.Save();
        canvas.Scale(DisplayScale);
        canvas.Translate(left, top);

        // Only zoom here. Preview->Full is already handled by DrawImage(dest=FULL)
        canvas.Scale(zoomScale);
        drawContent(canvas);

        canvas.Restore();
    }

    /// <summary>
    /// Pushes the current status text to the StatusLayer.
    /// Decision logic stays here (closest to composite and drop state);
    /// UIManager just forwards to the layer.
    /// </summary>
    private void UpdateStatusOverlay()
    {
        var textColor = _backgroundMode == BackgroundMode.White ? SKColors.Black : SKColors.White;

        var drop = _dropProgressProvider.GetDropStatus();
        if (drop is { Active: true, FilesEnumerated: > 300 })
        {
            UIManager.SetStatusOverlay($"{drop.FilesSupported} images found, {drop.FilesEnumerated} files scanned...", textColor);
            return;
        }

        if (_composite == null || _composite.State == CompositeState.Failed)
        {
            UIManager.SetStatusOverlay("No image", textColor);
            return;
        }

        if (_composite.State == CompositeState.Loading)
        {
            UIManager.SetStatusOverlay("Loading...", textColor);
            return;
        }

        UIManager.SetStatusOverlay(null, default);
    }

    private void DrawCheckerboardPattern(SKCanvas canvas)
    {
        var squareSize = (int)(24 * DisplayScale);

        using var lightGray = new SKPaint();
        lightGray.Color = new SKColor(120, 120, 120);
        lightGray.IsAntialias = false;

        using var darkGray = new SKPaint();
        darkGray.Color = new SKColor(80, 80, 80);
        darkGray.IsAntialias = false;

        for (var y = 0; y < WindowHeight; y += squareSize)
        for (var x = 0; x < WindowWidth; x += squareSize)
        {
            var rect = new SKRect(x, y, x + squareSize, y + squareSize);
            canvas.DrawRect(rect, ((x / squareSize + y / squareSize) % 2 == 0) ? lightGray : darkGray);
        }
    }

    public ApplicationStates GetApplicationStates()
    {
        var navigation = DirectoryNavigator.GetNavigation();
        var drop = _dropProgressProvider.GetDropStatus();

        return new ApplicationStates
        {
            CollectionType = DirectoryNavigator.GetCollectionType(),
            CollectionIndex = navigation.CollectionIndex,
            CollectionCount = navigation.CollectionCount,
            DirectoryIndex = navigation.DirectoryIndex,
            DirectoryCount = navigation.DirectoryCount,
            Zoom = _zoomPercentage,
            DisplayMode = _displayMode,
            SamplingMode = GetSamplingModeDescription(),
            InfoVisible = _infoVisible,
            HelpVisible = _helpVisible,
            SidebarVisible = _sidebarVisible,
            DropActive = drop.Active,
            DropAborted = drop.Aborted,
            DropPathsEnqueued = drop.PathsEnqueued,
            DropFilesEnumerated = drop.FilesEnumerated,
            DropFilesSupported = drop.FilesSupported,
            Backend = _backend
        };
    }

    private string GetSamplingModeDescription()
    {
        return _composite?.Content?.Kind == CompositeContentKind.Vector
            ? "Disabled (resolution-independent)"
            : _samplingMode.Description();
    }

    public void OnDrawableSizeChanged(DrawableSizeChangedEvent e)
    {
        WindowWidth = e.PixelWidth;
        WindowHeight = e.PixelHeight;
        DisplayScale = e.Scale;

        OnDrawableSizeChangedInternal(WindowWidth, WindowHeight, DisplayScale);

        UIManager.DisplayScale = DisplayScale;
        UIManager.Invalidate();
    }

    public void SetComposite(Composite? composite) => _composite = composite;

    public void SetOffset(SKPoint offset) => _offset = offset;

    public void SetDisplayMode(DisplayMode displayMode) => _displayMode = displayMode;

    public void SetZoom(int zoomPercentage) => _zoomPercentage = zoomPercentage;

    public void ToggleSampling()
    {
        if (_composite?.Content?.Kind != CompositeContentKind.Vector)
            _samplingMode = (SamplingMode)(((int)_samplingMode + 1) % Enum.GetValues<SamplingMode>().Length);
    }

    public void ToggleBackground()
        => _backgroundMode = (BackgroundMode)(((int)_backgroundMode + 1) % Enum.GetValues<BackgroundMode>().Length);

    public void ToggleInfo() => _infoVisible = !_infoVisible;

    public void ToggleHelp() => _helpVisible = !_helpVisible;

    public void ToggleSidebar() => _sidebarVisible = !_sidebarVisible;

    public UISettings ExportUiSettings()
    {
        return new UISettings(_samplingMode, _backgroundMode, _infoVisible, _helpVisible, _sidebarVisible);
    }

    public virtual void Dispose()
    {
        Unsubscribe<DrawableSizeChangedEvent>(OnDrawableSizeChanged);
        UIManager.Dispose();
    }

    private static SKRect ComputeVisibleFullRect(float imageW, float imageH, int windowPxW, int windowPxH, float displayScale, int zoomPercentage, SKPoint offsetPx)
    {
        var zoom = zoomPercentage / 100f;

        // Window in logical units
        var logicalW = windowPxW / displayScale;
        var logicalH = windowPxH / displayScale;

        // Image drawn size in logical units
        var drawW = imageW * zoom;
        var drawH = imageH * zoom;

        // Image top-left in logical window space (matches RenderCentered)
        var imageLeft = (logicalW - drawW) / 2f + offsetPx.X / displayScale;
        var imageTop = (logicalH - drawH) / 2f + offsetPx.Y / displayScale;

        // Convert window rect (0..logicalW/H) to image-space by inverse transform
        var visible = new SKRect(
            (0 - imageLeft) / zoom,
            (0 - imageTop) / zoom,
            (logicalW - imageLeft) / zoom,
            (logicalH - imageTop) / zoom
        );

        // Clamp to image bounds
        var bounds = new SKRect(0, 0, imageW, imageH);
        return SKRect.Intersect(visible, bounds);
    }
}