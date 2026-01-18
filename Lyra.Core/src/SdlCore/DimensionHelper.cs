using Lyra.Imaging.Content;
using static SDL3.SDL;

namespace Lyra.SdlCore;

public static class DimensionHelper
{
    public static DisplayMode GetInitialDisplayMode(IntPtr window, Composite? composite, out int zoomPercentage)
    {
        zoomPercentage = 100;

        if (composite == null || composite.IsEmpty)
            return DisplayMode.Undefined;

        var drawableBounds = GetDrawableSize(window, out var displayScale);

        var windowLogicalWidth = drawableBounds.Width / displayScale;
        var windowLogicalHeight = drawableBounds.Height / displayScale;

        var compositeLogicalWidth = composite.LogicalWidth;
        var compositeLogicalHeight = composite.LogicalHeight;
        
        if (compositeLogicalWidth <= windowLogicalWidth && compositeLogicalHeight <= windowLogicalHeight)
            return DisplayMode.OriginalImageSize;

        zoomPercentage = GetZoomToFitScreen(window, compositeLogicalWidth, compositeLogicalHeight);
        return DisplayMode.FitToScreen;
    }

    public static DrawableBounds GetDrawableSize(IntPtr window, out float displayScale)
    {
        GetWindowSize(window, out var logicalW, out var logicalH);
        displayScale = GetWindowDisplayScale(window);
        return new DrawableBounds((int)(logicalW * displayScale), (int)(logicalH * displayScale));
    }

    public static int GetZoomToFitScreen(IntPtr window, float imageWidth, float imageHeight)
    {
        var drawableBounds = GetDrawableSize(window, out _);
        var displayScale = GetWindowDisplayScale(window);

        var physicalZoomFactor = MathF.Min(drawableBounds.Width / imageWidth, drawableBounds.Height / imageHeight);

        return (int)MathF.Round((physicalZoomFactor * 100f) / displayScale);
    }
}