using Lyra.Common.Settings.Enums;
using Lyra.Imaging.Content;
using static SDL3.SDL;

namespace Lyra.SdlCore;

public static class DimensionHelper
{
    public const float MinZoom = 0.01f;
    public const float MaxZoom = 10000f;
    public const float ActualSize = 100f;

    private const float ZoomStep = 1.05f;
    private const float FitSnapTolerance = 0.001f;

    public static bool IsActualSize(float zoomPercentage) => MathF.Abs(zoomPercentage - ActualSize) < 0.01f;

    public static DisplayMode GetDisplayMode(IntPtr window, Composite? composite, InitDisplayMode initDisplayMode, out float zoomPercentage)
    {
        zoomPercentage = ActualSize;

        if (composite == null || composite.IsEmpty)
            return DisplayMode.Undefined;

        var drawable = GetDrawableSize(window);
        var windowLogicalWidth = drawable.PixelWidth / drawable.ContentScale;
        var windowLogicalHeight = drawable.PixelHeight / drawable.ContentScale;

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
    
    public static float GetNextZoom(float currentZoom, float direction)
    {
        var candidate = direction > 0
            ? currentZoom * ZoomStep
            : currentZoom / ZoomStep;

        return Math.Clamp(candidate, MinZoom, MaxZoom);
    }
    
    public static bool ReachesZoomToFit(float currentZoom, float candidateZoom, float fitZoom)
    {
        if (fitZoom <= 0 || !float.IsFinite(fitZoom))
            return false;

        var tolerance = fitZoom * FitSnapTolerance;

        // A step that starts at fit is leaving it, not reaching it.
        if (MathF.Abs(currentZoom - fitZoom) <= tolerance)
            return false;

        if (MathF.Abs(candidateZoom - fitZoom) <= tolerance)
            return true;

        return currentZoom < fitZoom
            ? candidateZoom > fitZoom
            : candidateZoom < fitZoom;
    }

    public static float GetZoomToFitScreen(IntPtr window, float imageWidth, float imageHeight)
    {
        var drawableBounds = GetDrawableSize(window);
        return GetZoomToFitScreen(imageWidth, imageHeight, drawableBounds.PixelWidth, drawableBounds.PixelHeight, drawableBounds.ContentScale);
    }

    public static float GetZoomToFitScreen(float imageWidth, float imageHeight, int pixelWidth, int pixelHeight, float contentScale)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || pixelWidth <= 0 || pixelHeight <= 0 || contentScale <= 0)
            return ActualSize;

        var physicalZoomFactor = MathF.Min(pixelWidth / imageWidth, pixelHeight / imageHeight);
        var zoom = (physicalZoomFactor * 100f) / contentScale;

        return float.IsFinite(zoom) ? Math.Clamp(zoom, MinZoom, MaxZoom) : ActualSize;
    }
}