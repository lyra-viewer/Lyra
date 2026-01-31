using Lyra.Imaging.Content;
using SkiaSharp;

namespace Lyra.Renderer;

public class SkiaCompositeContentDrawer : ICompositeContentDrawer
{
    public void Draw(SKCanvas canvas, Composite composite, SKRect destFullRect, SKRect visibleFullRect, SKSamplingOptions sampling, float zoomScale, float displayScale)
    {
        var content = composite.Content;
        if (content is null)
            return;

        switch (content)
        {
            case RasterContent raster:
                canvas.DrawImage(raster.Image, destFullRect, sampling);
                break;

            case VectorContent vector:
                DrawPictureScaled(canvas, vector.Picture, destFullRect);
                break;

            case RasterLargeContent large:
            {
                DrawRasterLarge(canvas, composite, destFullRect, visibleFullRect, large, sampling, zoomScale, displayScale);
                break;
            }
        }
    }

    private static void DrawPictureScaled(SKCanvas canvas, SKPicture picture, SKRect destFullRect)
    {
        var src = picture.CullRect;
        if (src.Width <= 0 || src.Height <= 0)
            return;

        canvas.Save();
        canvas.Translate(destFullRect.Left, destFullRect.Top);
        canvas.Scale(destFullRect.Width / src.Width, destFullRect.Height / src.Height);
        canvas.Translate(-src.Left, -src.Top); // normalize origin
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    private static void DrawRasterLarge(SKCanvas canvas, Composite composite, SKRect destFullRect, SKRect visibleFullRect, RasterLargeContent rasterLarge, SKSamplingOptions sampling, float zoomScale, float displayScale)
    {
        // Decide if preview is sharp enough at current zoom.
        // Yes -> draw preview only (skip tiles).
        // No  -> draw preview (as background) + tiles over it.
        var hasPreview = rasterLarge.PreviewImage != null;
        var hasTiles = rasterLarge.TileSource != null;

        if (hasPreview)
            canvas.DrawImage(rasterLarge.PreviewImage, destFullRect, sampling);

        if (!hasTiles)
            return;

        // Safety: avoid NaN/Inf if something isn't ready yet
        if (composite.LogicalWidth <= 0 || composite.LogicalHeight <= 0)
            return;

        // Safety: if there's no preview, rely on tiles.
        if (!hasPreview)
        {
            foreach (var tile in rasterLarge.TileSource.GetTiles(visibleFullRect, new SKSize(composite.LogicalWidth, composite.LogicalHeight)))
                canvas.DrawImage(tile.Image, tile.DestRect, sampling);
            return;
        }

        // Pixels-per-full-unit provided by preview
        var previewPpfuX = rasterLarge.PreviewImage.Width / composite.LogicalWidth;
        var previewPpfuY = rasterLarge.PreviewImage.Height / composite.LogicalHeight;
        var previewPpfu = MathF.Min(previewPpfuX, previewPpfuY);

        // Pixels-per-full-unit required by current view
        var screenPpfu = zoomScale * displayScale;

        // Start tiling when demand exceeds what the preview can provide (with a small tolerance).
        // Example: 1.05 means it allows up to 5% upscale of the preview before switching to tiles.
        const float tileThreshold = 1.05f;

        var useTiles = screenPpfu > previewPpfu * tileThreshold;
        if (!useTiles)
            return;

        foreach (var tile in rasterLarge.TileSource.GetTiles(visibleFullRect, new SKSize(composite.LogicalWidth, composite.LogicalHeight))) 
            canvas.DrawImage(tile.Image, tile.DestRect, sampling);
    }
}