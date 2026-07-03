using Lyra.Common.Settings.Enums;
using Lyra.Imaging.Content;
using static SDL3.SDL;

namespace Lyra.SdlCore;

public static class DimensionHelper
{
    public static DisplayMode GetDisplayMode(IntPtr window, Composite? composite, InitDisplayMode initDisplayMode, out int zoomPercentage)
    {
        zoomPercentage = 100;

        if (composite == null || composite.IsEmpty)
            return DisplayMode.Undefined;

        GetWindowSize(window, out var windowLogicalWidth, out var windowLogicalHeight);

        var compositeLogicalWidth = composite.LogicalWidth;
        var compositeLogicalHeight = composite.LogicalHeight;

        var fitsInWindow = compositeLogicalWidth <= windowLogicalWidth && compositeLogicalHeight <= windowLogicalHeight;

        var shouldFit = initDisplayMode switch
        {
            InitDisplayMode.FitAll => true,
            InitDisplayMode.FitLarge => !fitsInWindow,
            InitDisplayMode.FitSmall => fitsInWindow,
            InitDisplayMode.ActualSize => false,
            _ => !fitsInWindow
        };

        if (!shouldFit)
            return DisplayMode.OriginalImageSize;

        zoomPercentage = GetZoomToFitScreen(window, compositeLogicalWidth, compositeLogicalHeight);
        return DisplayMode.FitToScreen;
    }

    public static PixelSize GetDrawableSize(IntPtr window)
    {
        GetWindowSize(window, out var logicalWidth, out var logicalHeight);
        
        if (!GetWindowSizeInPixels(window, out var pixelWidth, out var pixelHeight)
            || pixelWidth <= 0 || pixelHeight <= 0)
        {
            pixelWidth = logicalWidth;
            pixelHeight = logicalHeight;
        }

        return new PixelSize(pixelWidth, pixelHeight, GetPixelDensity(window), GetContentScale(window));
    }
    
    public static float GetContentScale(IntPtr window)
    {
        var scale = GetWindowDisplayScale(window);
        return scale > 0f ? scale : 1f;
    }
    
    public static float GetPixelDensity(IntPtr window)
    {
        GetWindowSize(window, out var logicalWidth, out _);
        if (logicalWidth <= 0 || !GetWindowSizeInPixels(window, out var pixelWidth, out _) || pixelWidth <= 0)
            return 1f;

        return (float)pixelWidth / logicalWidth;
    }

    public static int GetZoomToFitScreen(IntPtr window, float imageWidth, float imageHeight)
    {
        var drawableBounds = GetDrawableSize(window);

        var physicalZoomFactor = MathF.Min(drawableBounds.PixelWidth / imageWidth, drawableBounds.PixelHeight / imageHeight);

        return (int)MathF.Round((physicalZoomFactor * 100f) / drawableBounds.Scale);
    }
}