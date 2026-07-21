using Lyra.Common.Events;
using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;
using Lyra.DropStatusProvider;
using Lyra.DuplicateStatusProvider;
using Lyra.FileLoader.Navigation;
using Lyra.FileLoader.Store;
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
    protected float ContentScale { get; private set; }

    private Composite? _composite;
    private SKPoint _offset = SKPoint.Empty;
    private DisplayMode _displayMode = DisplayMode.Undefined;
    private int _zoomPercentage = 100;

    private readonly ViewState _viewState;
    private readonly ICompositeContentDrawer _contentDrawer;
    private readonly IDropProgressProvider _dropProgressProvider;
    private readonly IScanProgressProvider? _scanProgressProvider;

    public UIManager UIManager { get; private set; }

    private readonly string _backend;

    protected SkiaRendererBase(PixelSize drawableSize, IDropProgressProvider dropProgressProvider, ViewState viewState, string backend, IScanProgressProvider? scanProgressProvider = null)
    {
        ArgumentNullException.ThrowIfNull(dropProgressProvider);
        ArgumentNullException.ThrowIfNull(viewState);

        _backend = backend;
        _viewState = viewState;

        WindowWidth = drawableSize.PixelWidth;
        WindowHeight = drawableSize.PixelHeight;
        DisplayScale = drawableSize.Scale;
        ContentScale = drawableSize.ContentScale;

        _dropProgressProvider = dropProgressProvider;
        _scanProgressProvider = scanProgressProvider;

        Subscribe<DrawableSizeChangedEvent>(OnDrawableSizeChanged);

        _contentDrawer = new SkiaCompositeContentDrawer();

        UIManager = new UIManager(ContentScale)
        {
            PointerScale = DisplayScale / ContentScale
        };
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
        canvas.Scale(ContentScale);
        UIManager.Render(canvas);
        canvas.Restore();
    }

    public void RefreshUI(Composite? composite)
    {
        UIManager.Refresh(UIState.Create(composite, GetApplicationStates(), DirectoryNavigator.GetSnapshot(), DirectoryNavigator.GetNavigation(), FileRecordDatabase.Current));
    }

    private void RenderBackground(SKCanvas canvas)
    {
        switch (_viewState.BackgroundMode)
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

        var sampling = _viewState.SamplingMode switch
        {
            SamplingMode.Smooth => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            SamplingMode.Pixel or _ => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
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
                displayScale: ContentScale,
                zoomPercentage: _zoomPercentage,
                offsetPx: _offset
            );

            _contentDrawer.Draw(c, _composite, destFull, visibleFull, sampling, zoomScale, ContentScale);
        });
    }

    private void RenderCentered(SKCanvas canvas, SKSize logicalSize, Action<SKCanvas> drawContent)
    {
        var zoomScale = _zoomPercentage / 100f;

        var drawWidth = logicalSize.Width * zoomScale;
        var drawHeight = logicalSize.Height * zoomScale;
        
        var logicalWindowWidth = WindowWidth / ContentScale;
        var logicalWindowHeight = WindowHeight / ContentScale;

        var left = (logicalWindowWidth - drawWidth) / 2 + _offset.X / ContentScale;
        var top = (logicalWindowHeight - drawHeight) / 2 + _offset.Y / ContentScale;

        canvas.Save();
        canvas.Scale(ContentScale);
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
        var textColor = _viewState.BackgroundMode == BackgroundMode.White ? SKColors.Black : SKColors.White;

        if (_scanProgressProvider?.GetScanStatus() is { Active: true } scan)
        {
            UIManager.SetStatusOverlay(FormatScanStatus(scan), textColor);
            return;
        }

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

    private static string FormatScanStatus(ScanProgress scan) => scan.Phase switch
    {
        ScanPhase.Exact when scan.Total > 0      => $"Finding duplicates… hashing {scan.Done}/{scan.Total}",
        ScanPhase.Exact                          => "Finding duplicates… measuring sizes",
        ScanPhase.Perceptual when scan.Total > 0 => $"Perceptual scan… {scan.Done}/{scan.Total}",
        ScanPhase.Perceptual                     => "Perceptual scan…",
        _                                        => "Finding duplicates…"
    };

    private void DrawCheckerboardPattern(SKCanvas canvas)
    {
        var squareSize = (int)(24 * ContentScale);

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
        var scan = _scanProgressProvider?.GetScanStatus() ?? default;

        return new ApplicationStates
        {
            CollectionType = DirectoryNavigator.GetCollectionType(),
            CollectionIndex = navigation.CollectionIndex,
            CollectionCount = navigation.CollectionCount,
            DirectoryIndex = navigation.DirectoryIndex,
            DirectoryCount = navigation.DirectoryCount,
            InDuplicatesMode = DirectoryNavigator.IsDuplicatesMode,
            Zoom = _zoomPercentage,
            DisplayMode = _displayMode,
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
            Backend = _backend
        };
    }

    private string GetSamplingModeDescription()
    {
        return _composite?.Content?.Kind == CompositeContentKind.Vector
            ? "Disabled (resolution-independent)"
            : _viewState.SamplingMode.Description();
    }

    public void OnDrawableSizeChanged(DrawableSizeChangedEvent e)
    {
        WindowWidth = e.PixelWidth;
        WindowHeight = e.PixelHeight;
        DisplayScale = e.Scale;
        ContentScale = e.ContentScale;

        OnDrawableSizeChangedInternal(WindowWidth, WindowHeight, DisplayScale);

        UIManager.DisplayScale = ContentScale;
        UIManager.PointerScale = DisplayScale / ContentScale;
        UIManager.Invalidate();
    }

    public void SetComposite(Composite? composite) => _composite = composite;

    public void SetOffset(SKPoint offset) => _offset = offset;

    public void SetDisplayMode(DisplayMode displayMode) => _displayMode = displayMode;

    public void SetZoom(int zoomPercentage) => _zoomPercentage = zoomPercentage;
    
    public bool IsCompositeVector => _composite?.Content?.Kind == CompositeContentKind.Vector;

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