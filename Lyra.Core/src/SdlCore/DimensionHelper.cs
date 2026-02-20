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

        GetWindowSize(window, out var windowLogicalWidth, out var windowLogicalHeight);

        var compositeLogicalWidth = composite.LogicalWidth;
        var compositeLogicalHeight = composite.LogicalHeight;
        
        if (compositeLogicalWidth <= windowLogicalWidth && compositeLogicalHeight <= windowLogicalHeight)
            return DisplayMode.OriginalImageSize;

        zoomPercentage = GetZoomToFitScreen(window, compositeLogicalWidth, compositeLogicalHeight);
        return DisplayMode.FitToScreen;
    }

    public static PixelSize GetDrawableSize(IntPtr window)
    {
        GetWindowSize(window, out var logicalWidth, out var logicalHeight);
        var displayScale = GetWindowDisplayScale(window);
        return new PixelSize((int)(logicalWidth * displayScale), (int)(logicalHeight * displayScale), displayScale);
    }

    public static int GetZoomToFitScreen(IntPtr window, float imageWidth, float imageHeight)
    {
        var drawableBounds = GetDrawableSize(window);
        var displayScale = GetWindowDisplayScale(window);

        var physicalZoomFactor = MathF.Min(drawableBounds.PixelWidth / imageWidth, drawableBounds.PixelHeight / imageHeight);

        return (int)MathF.Round((physicalZoomFactor * 100f) / displayScale);
    }
}