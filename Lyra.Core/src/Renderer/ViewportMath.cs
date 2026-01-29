using SkiaSharp;

namespace Lyra.Renderer;

public static class ViewportMath
{
    public static SKRect ComputeVisibleFullRect(float imageW, float imageH, int windowPxW, int windowPxH, float displayScale, int zoomPercentage, SKPoint offsetPx)
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