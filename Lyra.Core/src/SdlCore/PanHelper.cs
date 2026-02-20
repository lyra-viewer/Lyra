using Lyra.Imaging.Content;
using SkiaSharp;
using static Lyra.SdlCore.DimensionHelper;

namespace Lyra.SdlCore;

public class PanHelper(IntPtr window, Composite composite, int zoomPercentage)
{
    private int _zoomPercentage = zoomPercentage;
    private SKPoint _lastMousePosition;

    public SKPoint CurrentOffset { get; set; } = SKPoint.Empty;

    public void UpdateZoom(int zoomPercentage)
    {
        _zoomPercentage = zoomPercentage;
    }

    public void Start(float rawX, float rawY)
    {
        var scale = GetDrawableSize(window).Scale;
        _lastMousePosition = new SKPoint(rawX * scale, rawY * scale);
    }

    public bool CanPan()
    {
        var (contentW, contentH, bounds) = GetZoomedContentAndBounds();
        return contentW * bounds.Scale > bounds.PixelWidth || contentH * bounds.Scale > bounds.PixelHeight;
    }

    public void Move(float rawX, float rawY)
    {
        var scale = GetDrawableSize(window).Scale;

        var current = new SKPoint(rawX * scale, rawY * scale);
        var delta = current - _lastMousePosition;
        _lastMousePosition = current;

        CurrentOffset += delta;
        Clamp();
    }

    public void Clamp()
    {
        var (contentW, contentH, bounds) = GetZoomedContentAndBounds();

        if (contentW * bounds.Scale <= bounds.PixelWidth && contentH * bounds.Scale <= bounds.PixelHeight)
        {
            CurrentOffset = SKPoint.Empty;
            return;
        }

        var maxOffsetX = Math.Max(0, (contentW * bounds.Scale - bounds.PixelWidth) / 2);
        var maxOffsetY = Math.Max(0, (contentH * bounds.Scale - bounds.PixelHeight) / 2);

        CurrentOffset = new SKPoint(
            Math.Clamp(CurrentOffset.X, -maxOffsetX, maxOffsetX),
            Math.Clamp(CurrentOffset.Y, -maxOffsetY, maxOffsetY)
        );
    }

    public SKPoint GetOffsetForZoomAtCursor(SKPoint mouse, int newZoom)
    {
        if (composite.IsEmpty)
            return CurrentOffset;

        var oldScale = _zoomPercentage / 100f;
        var newScale = newZoom / 100f;

        var imageDrawSizeOld = new SKSize(composite.LogicalWidth * oldScale, composite.LogicalHeight * oldScale);
        var imageDrawSizeNew = new SKSize(composite.LogicalWidth * newScale, composite.LogicalHeight * newScale);

        var (_, _, bounds) = GetZoomedContentAndBounds();

        var imageTopLeftOld = new SKPoint(
            (bounds.PixelWidth - imageDrawSizeOld.Width) / 2 + CurrentOffset.X,
            (bounds.PixelHeight - imageDrawSizeOld.Height) / 2 + CurrentOffset.Y
        );

        var cursorToImage = mouse - imageTopLeftOld;
        var normalizedPos = new SKPoint(
            cursorToImage.X / imageDrawSizeOld.Width,
            cursorToImage.Y / imageDrawSizeOld.Height
        );

        var imageTopLeftNew = mouse - new SKPoint(
            imageDrawSizeNew.Width * normalizedPos.X,
            imageDrawSizeNew.Height * normalizedPos.Y
        );

        return imageTopLeftNew - new SKPoint(
            (bounds.PixelWidth - imageDrawSizeNew.Width) / 2,
            (bounds.PixelHeight - imageDrawSizeNew.Height) / 2
        );
    }

    private (int scaledWidth, int scaledHeight, PixelSize bounds) GetZoomedContentAndBounds()
    {
        var drawableSize = GetDrawableSize(window);

        var width = composite.LogicalWidth;
        var height = composite.LogicalHeight;

        if (width <= 0 || height <= 0)
            return (0, 0, drawableSize);

        var zoomScale = _zoomPercentage / 100f;
        var scaledWidth = (int)(width * zoomScale);
        var scaledHeight = (int)(height * zoomScale);

        return (scaledWidth, scaledHeight, drawableSize);
    }
}