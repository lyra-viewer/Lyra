using Lyra.Common.Events;
using Lyra.Common.Settings;
using Lyra.Common.Settings.Enums;
using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Renderer.Display;
using Lyra.Renderer.Drawing;
using SkiaSharp;
using static Lyra.Common.Events.EventManager;

namespace Lyra.Renderer.Backends;

public abstract class SkiaRendererBase : IDisposable, IDrawableSizeAware
{
    protected int WindowWidth { get; private set; }
    protected int WindowHeight { get; private set; }

    protected float DisplayScale { get; private set; }
    protected float ContentScale { get; private set; }
    
    protected IDisplayCapabilityService Displays { get; private set; } = DisplayCapabilityService.None;
    
    public virtual bool BackendSupportsExtendedRange => false;

    private readonly ICompositeContentDrawer _contentDrawer;

    public string Backend { get; }

    /// One-shot diagnostic: whether a frame carried values above SDR white.
    private readonly PresentedPeak _presentedPeak = new();

    protected SkiaRendererBase(PixelSize drawableSize, string backend)
    {
        Backend = backend;

        WindowWidth = drawableSize.PixelWidth;
        WindowHeight = drawableSize.PixelHeight;
        DisplayScale = drawableSize.Scale;
        ContentScale = drawableSize.ContentScale;

        _contentDrawer = new SkiaCompositeContentDrawer();
    }

    /// <summary>
    /// How much GPU memory Skia may keep for cached resources - textures, atlases, scratch
    /// surfaces - before it starts evicting.
    /// </summary>
    internal const int ResourceCacheLimitBytes = 512 * 1024 * 1024;

    protected static void ConfigureResourceCache(GRContext context, string backend)
    {
        var before = context.GetResourceCacheLimit();
        context.SetResourceCacheLimit(ResourceCacheLimitBytes);

        Logger.Debug($"[{backend}] GPU resource cache limit {before / 1024 / 1024} MB -> {ResourceCacheLimitBytes / 1024 / 1024} MB.");
    }

    /// <summary>
    /// Starts listening for window changes. Call once, on a renderer that finished constructing.
    /// </summary>
    /// <remarks>
    /// Deliberately not done in the constructor. The event bus is static and holds a strong
    /// reference, so a renderer that subscribed and then failed to construct - which every backend
    /// can do, and which the backend fallback exists to survive - would never be handed to anyone
    /// able to unsubscribe it. It would go on receiving resize events, with no surface to apply
    /// them to, for the life of the process.
    /// </remarks>
    public void Attach()
    {
        if (_attached)
            return;

        _attached = true;
        Subscribe<DrawableSizeChangedEvent>(OnDrawableSizeChanged);
    }

    private bool _attached;

    /// <summary>
    /// Hands the renderer the service that tracks the current display's EDR capability. Called by
    /// the core once the window exists, which is necessarily after the renderer is constructed.
    /// </summary>
    public void SetDisplayCapabilities(IDisplayCapabilityService displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        Displays = displays;
    }

    /// <summary>
    /// What the window surface is in color terms: its space, and whether it can carry light above
    /// SDR white. Not a constant - the OpenGL backend reads its color space from the display's
    /// ICC profile.
    /// </summary>
    protected abstract SurfaceProfile Surface { get; }
    
    protected abstract SKSurface? CreateSurface();

    protected virtual bool DisposeSurfaceAfterRender => true;

    protected virtual void BeforeRender()
    {
    }

    protected virtual void AfterRender(SKSurface surface)
    {
    }

    /// <summary>
    /// Releases whatever <see cref="BeforeRender"/> and <see cref="CreateSurface"/> acquired.
    /// Runs after every frame, including one that produced no surface or threw partway through -
    /// an autorelease pool or a retained drawable left behind by a failed frame is never
    /// reclaimed, because the next frame overwrites the field holding it.
    /// </summary>
    protected virtual void EndFrame()
    {
    }

    protected virtual void OnDrawableSizeChangedInternal(int width, int height, float scale)
    {
    }

    public virtual void Render(in RenderScene scene, IRenderOverlay? overlay)
    {
        BeforeRender();

        try
        {
            var surface = CreateSurface();
            if (surface is null)
                return;

            try
            {
                var canvas = surface.Canvas;

                RenderBackground(canvas, scene.Background);
                RenderComposite(canvas, scene);

                if (overlay is not null)
                    RenderOverlay(canvas, overlay);

                // Debug only: it reads the whole frame back from the GPU, for one log line.
                if (SettingsManager.AppSettings.Debug)
                    _presentedPeak.Observe(surface, Backend, scene.Composite?.Content is not null);

                AfterRender(surface);
            }
            finally
            {
                if (DisposeSurfaceAfterRender)
                    surface.Dispose();
            }
        }
        finally
        {
            EndFrame();
        }
    }

    private void RenderOverlay(SKCanvas canvas, IRenderOverlay overlay)
    {
        canvas.Save();
        canvas.Scale(ContentScale);
        overlay.Render(canvas);
        canvas.Restore();
    }

    private void RenderBackground(SKCanvas canvas, BackgroundMode background)
    {
        switch (background)
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

    private void RenderComposite(SKCanvas canvas, RenderScene scene)
    {
        if (scene.Composite is not { Content: not null } composite)
            return;
        
        if (!scene.HasView)
            return;

        var sampling = scene.Sampling switch
        {
            SamplingMode.Smooth => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            SamplingMode.Pixel or _ => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
        };

        var logicalSize = new SKSize(composite.LogicalWidth, composite.LogicalHeight);
        var zoomScale = scene.ZoomPercentage / 100f;

        RenderCentered(canvas, logicalSize, scene.ZoomPercentage, scene.Offset, c =>
        {
            var destFull = new SKRect(0, 0, logicalSize.Width, logicalSize.Height);

            var visibleFull = ComputeVisibleFullRect(
                imageW: logicalSize.Width,
                imageH: logicalSize.Height,
                windowPxW: WindowWidth,
                windowPxH: WindowHeight,
                displayScale: ContentScale,
                zoomPercentage: scene.ZoomPercentage,
                offsetPx: scene.Offset
            );

            _contentDrawer.Draw(c, composite, destFull, visibleFull, sampling, zoomScale, ContentScale, Surface);
        });
    }

    private void RenderCentered(SKCanvas canvas, SKSize logicalSize, float zoomPercentage, SKPoint offset, Action<SKCanvas> drawContent)
    {
        var zoomScale = zoomPercentage / 100f;

        var drawWidth = logicalSize.Width * zoomScale;
        var drawHeight = logicalSize.Height * zoomScale;
        
        var logicalWindowWidth = WindowWidth / ContentScale;
        var logicalWindowHeight = WindowHeight / ContentScale;

        var left = (logicalWindowWidth - drawWidth) / 2 + offset.X / ContentScale;
        var top = (logicalWindowHeight - drawHeight) / 2 + offset.Y / ContentScale;

        canvas.Save();
        canvas.Scale(ContentScale);
        canvas.Translate(left, top);

        // Only zoom here. Preview->Full is already handled by DrawImage(dest=FULL)
        canvas.Scale(zoomScale);
        drawContent(canvas);

        canvas.Restore();
    }

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

    public void OnDrawableSizeChanged(DrawableSizeChangedEvent e)
    {
        WindowWidth = e.PixelWidth;
        WindowHeight = e.PixelHeight;
        DisplayScale = e.Scale;
        ContentScale = e.ContentScale;

        OnDrawableSizeChangedInternal(WindowWidth, WindowHeight, DisplayScale);
    }

    public virtual void Dispose()
    {
        if (_attached)
        {
            _attached = false;
            Unsubscribe<DrawableSizeChangedEvent>(OnDrawableSizeChanged);
        }
    }

    private static SKRect ComputeVisibleFullRect(float imageW, float imageH, int windowPxW, int windowPxH, float displayScale, float zoomPercentage, SKPoint offsetPx)
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