using Lyra.Common.Events;
using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using Lyra.Common.SystemExtensions;
using Lyra.DropStatusProvider;
using Lyra.FileLoader;
using Lyra.Imaging.Content;
using Lyra.Renderer.Overlay;
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

    private SamplingMode _samplingMode = SamplingMode.Cubic;
    private BackgroundMode _backgroundMode = BackgroundMode.Black;
    private InfoMode _infoMode = InfoMode.Basic;
    private bool _helpBarVisible = true;

    private readonly ICompositeContentDrawer _contentDrawer;
    private readonly IDropStatusProvider _dropStatusProvider;

    private readonly ImageInfoOverlay _imageInfoOverlay;
    private readonly HelpBarOverlay _helpBarOverlay;
    private readonly CenteredTextOverlay _centeredOverlay;

    protected SkiaRendererBase(PixelSize drawableSize, IDropStatusProvider dropStatusProvider)
    {
        WindowWidth = drawableSize.PixelWidth;
        WindowHeight = drawableSize.PixelHeight;
        DisplayScale = drawableSize.Scale;
        
        _dropStatusProvider = dropStatusProvider;
        
        Subscribe<DrawableSizeChangedEvent>(OnDrawableSizeChanged);

        _imageInfoOverlay = new ImageInfoOverlay().WithDrawableSizeSubscription();
        _helpBarOverlay = new HelpBarOverlay().WithDrawableSizeSubscription();
        _centeredOverlay = new CenteredTextOverlay().WithDrawableSizeSubscription();

        _contentDrawer = new SkiaCompositeContentDrawer();
    }

    protected abstract SKSurface CreateSurface();

    protected virtual void BeforeRender()
    {
    }

    protected virtual void AfterRender(SKSurface surface)
    {
    }

    public virtual void Render()
    {
        BeforeRender();

        using var surface = CreateSurface();
        var canvas = surface.Canvas;

        RenderBackground(canvas);
        RenderComposite(canvas);
        RenderOverlay(canvas);

        canvas.Flush();

        AfterRender(surface);
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

            var visibleFull = ViewportMath.ComputeVisibleFullRect(
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

    private void RenderOverlay(SKCanvas canvas)
    {
        var bounds = new PixelSize(WindowWidth, WindowHeight, DisplayScale);
        var textColor = _backgroundMode == BackgroundMode.White ? SKColors.Black : SKColors.White;

        if (_infoMode != InfoMode.None)
            _imageInfoOverlay.Render(canvas, bounds, textColor, (_composite, GetApplicationStates()));

        if (_helpBarVisible)
            _helpBarOverlay.Render(canvas, bounds, textColor, (_composite, GetApplicationStates()));

        var drop = _dropStatusProvider.GetDropStatus();
        if (drop is { Active: true, FilesEnumerated: > 300 })
        {
            _centeredOverlay.Render(canvas, bounds, textColor, $"{drop.FilesSupported} images found, {drop.FilesEnumerated} files scanned...");
        }
        else
        {
            if (_composite == null || _composite.State == CompositeState.Failed)
                _centeredOverlay.Render(canvas, bounds, textColor, "No image");
            else if (_composite.State == CompositeState.Loading) 
                _centeredOverlay.Render(canvas, bounds, textColor, "Loading...");
        }
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

    private ApplicationStates GetApplicationStates()
    {
        var navigation = DirectoryNavigator.GetNavigation();
        var drop = _dropStatusProvider.GetDropStatus();

        return new ApplicationStates
        {
            CollectionType = DirectoryNavigator.GetCollectionType().Description(),
            CollectionIndex = navigation.CollectionIndex,
            CollectionCount = navigation.CollectionCount,
            DirectoryIndex = navigation.DirectoryIndex,
            DirectoryCount = navigation.DirectoryCount,
            Zoom = _zoomPercentage,
            DisplayMode = _displayMode.Description(),
            SamplingMode = GetSamplingModeDescription(),
            ShowExif = _infoMode == InfoMode.WithExif,
            DropActive = drop.Active,
            DropAborted = drop.Aborted,
            DropPathsEnqueued = drop.PathsEnqueued,
            DropFilesEnumerated = drop.FilesEnumerated,
            DropFilesSupported = drop.FilesSupported,
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

    public void ToggleInfo()
        => _infoMode = (InfoMode)(((int)_infoMode + 1) % Enum.GetValues<InfoMode>().Length);

    public void ToggleHelpBar()
        => _helpBarVisible = !_helpBarVisible;

    public void ApplyUserSettings(UiSettings uiSettings)
    {
        _samplingMode = uiSettings.SamplingMode;
        _backgroundMode = uiSettings.BackgroundMode;
        _infoMode = uiSettings.InfoLevel;
        _helpBarVisible = uiSettings.HelpBarVisible;
    }

    public UiSettings ExportUiSettings()
    {
        return new UiSettings(_samplingMode, _backgroundMode, _infoMode, _helpBarVisible);
    }
    
    public virtual void Dispose()
    {
        Unsubscribe<DrawableSizeChangedEvent>(OnDrawableSizeChanged);
    }
}