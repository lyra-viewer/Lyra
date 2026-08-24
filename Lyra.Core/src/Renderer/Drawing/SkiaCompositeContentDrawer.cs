using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;

namespace Lyra.Renderer.Drawing;

public class SkiaCompositeContentDrawer : ICompositeContentDrawer
{
    public void Draw(SKCanvas canvas, Composite composite, SKRect destFullRect, SKRect visibleFullRect, SKSamplingOptions sampling, float zoomScale, float displayScale, SurfaceProfile surface)
    {
        var content = composite.Content;
        if (content is null)
            return;

        // Unwrap first: a variant set draws whatever rendition is selected, which can be any
        // content type - so this defers to the same switch rather than assuming raster.
        while (content is VariantRasterContent variants)
            content = variants.Active;

        switch (content)
        {
            case HdrRasterContent hdr:
                DrawHdr(canvas, hdr, destFullRect, sampling, surface);
                break;

            case RasterContent raster:
                canvas.DrawImage(raster.Image, destFullRect, sampling);
                break;

            case VectorContent vector:
                DrawPictureScaled(canvas, vector.Picture, destFullRect);
                break;

            case RasterLargeContent large:
            {
                DrawRasterLarge(canvas, composite, destFullRect, visibleFullRect, large, sampling, zoomScale, displayScale, surface);
                break;
            }
        }
    }
    
    private static void DrawPreview(SKCanvas canvas, RasterLargeContent rasterLarge, SKImage preview, SKRect destFullRect, SKSamplingOptions sampling, SurfaceProfile surface)
    {
        if (rasterLarge.PreviewWhitePoint is not { } whitePoint)
        {
            canvas.DrawImage(preview, destFullRect, sampling);
            return;
        }

        var localMatrix = SKMatrix.CreateScaleTranslation(
            destFullRect.Width / preview.Width,
            destFullRect.Height / preview.Height,
            destFullRect.Left,
            destFullRect.Top
        );

        var exposure = HdrDecodeSettings.ExposureScale;

        using var paint = HdrToneMapShader.CreatePaint(preview, sampling, localMatrix, HdrDecodeSettings.ToneMapMode, exposure, whitePoint * exposure, surface);
        if (paint is null)
        {
            canvas.DrawImage(preview, destFullRect, sampling);
            return;
        }

        canvas.DrawRect(destFullRect, paint);
    }

    /// <summary>
    /// Tone-maps at draw time from the current settings, so moving the exposure slider is a
    /// uniform change rather than a re-decode. Falls back to drawing the image as-is when the
    /// runtime effect is unavailable - washed out, but visible, which beats a blank canvas.
    /// </summary>
    private static void DrawHdr(SKCanvas canvas, HdrRasterContent hdr, SKRect destFullRect, SKSamplingOptions sampling, SurfaceProfile surface)
    {
        var image = hdr.Image;
        if (image.Width <= 0 || image.Height <= 0)
            return;

        var localMatrix = SKMatrix.CreateScaleTranslation(
            destFullRect.Width / image.Width,
            destFullRect.Height / image.Height,
            destFullRect.Left,
            destFullRect.Top
        );

        var exposure = HdrDecodeSettings.ExposureScale;

        using var paint = HdrToneMapShader.CreatePaint(
            image,
            sampling,
            localMatrix,
            HdrDecodeSettings.ToneMapMode,
            exposure,
            hdr.WhitePoint * exposure,
            surface
        );

        if (paint is null)
        {
            canvas.DrawImage(image, destFullRect, sampling);
            return;
        }

        canvas.DrawRect(destFullRect, paint);
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

    private static void DrawRasterLarge(SKCanvas canvas, Composite composite, SKRect destFullRect, SKRect visibleFullRect, RasterLargeContent rasterLarge, SKSamplingOptions sampling, float zoomScale, float displayScale, SurfaceProfile surface)
    {
        // Decide if preview is sharp enough at current zoom.
        // Yes -> draw preview only (skip tiles).
        // No  -> draw preview (as background) + tiles over it.
        var preview = rasterLarge.PreviewImage;
        var tileSource = rasterLarge.TileSource;

        if (preview != null)
            DrawPreview(canvas, rasterLarge, preview, destFullRect, sampling, surface);

        if (tileSource == null)
            return;

        // Safety: avoid NaN/Inf if something isn't ready yet
        if (composite.LogicalWidth <= 0 || composite.LogicalHeight <= 0)
            return;

        // Safety: if there's no preview, rely on tiles.
        if (preview == null)
        {
            DrawTiles(canvas, composite, rasterLarge, tileSource, visibleFullRect, sampling, surface);
            return;
        }

        // Pixels-per-full-unit provided by preview
        var previewPpfuX = preview.Width / composite.LogicalWidth;
        var previewPpfuY = preview.Height / composite.LogicalHeight;
        var previewPpfu = MathF.Min(previewPpfuX, previewPpfuY);

        // Pixels-per-full-unit required by current view
        var screenPpfu = zoomScale * displayScale;

        // Start tiling when demand exceeds what the preview can provide (with a small tolerance).
        // Example: 1.05 means it allows up to 5% upscale of the preview before switching to tiles.
        const float tileThreshold = 1.05f;

        var useTiles = screenPpfu > previewPpfu * tileThreshold;
        if (!useTiles)
            return;

        DrawTiles(canvas, composite, rasterLarge, tileSource, visibleFullRect, sampling, surface);
    }
    
    private static void DrawTiles(SKCanvas canvas, Composite composite, RasterLargeContent rasterLarge, ITileSource tileSource, SKRect visibleFullRect, SKSamplingOptions sampling, SurfaceProfile surface)
    {
        var fullSize = new SKSize(composite.LogicalWidth, composite.LogicalHeight);
        var whitePoint = rasterLarge.TileWhitePoint;

        foreach (var tile in tileSource.GetTiles(visibleFullRect, fullSize))
        {
            if (whitePoint is not { } measured)
            {
                canvas.DrawImage(tile.Image, tile.DestRect, sampling);
                continue;
            }

            var localMatrix = SKMatrix.CreateScaleTranslation(
                tile.DestRect.Width / tile.Image.Width,
                tile.DestRect.Height / tile.Image.Height,
                tile.DestRect.Left,
                tile.DestRect.Top
            );

            var exposure = HdrDecodeSettings.ExposureScale;

            using var paint = HdrToneMapShader.CreatePaint(tile.Image, sampling, localMatrix, HdrDecodeSettings.ToneMapMode, exposure, measured * exposure, surface);
            if (paint is null)
            {
                canvas.DrawImage(tile.Image, tile.DestRect, sampling);
                continue;
            }

            canvas.DrawRect(tile.DestRect, paint);
        }
    }
}