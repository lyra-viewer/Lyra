using Lyra.Imaging.Content;
using SkiaSharp;

namespace Lyra.Renderer;

public class SkiaCompositeContentDrawer : ICompositeContentDrawer
{
    public void Draw(SKCanvas canvas, Composite composite, SKRect destFullRect, SKSamplingOptions sampling)
    {
        var content = composite.Content;
        if (content is null)
            return;

        // TODO (when added tiling): compute visibleFullRect and pass it to the tiled branch.
        // For now, "visible == full" is safe and keeps API stable.
        var visibleFullRect = destFullRect;

        switch (content)
        {
            case RasterContent r:
                canvas.DrawImage(r.Image, destFullRect, sampling);
                break;

            case VectorContent v:
                DrawPictureScaled(canvas, v.Picture, destFullRect);
                break;

            case RasterLargeContent l:
                // Preview first (fast)
                if (l.PreviewImage != null)
                    canvas.DrawImage(l.PreviewImage, destFullRect, sampling);

                // Full image (optional) beats preview everywhere (TODO shouldn't be like this!)
                if (l.FullImage != null)
                    canvas.DrawImage(l.FullImage, destFullRect, sampling);

                // Tiles beat both where present (TODO still not sure)
                if (l.TileSource != null)
                {
                    foreach (var tile in l.TileSource.GetTiles(visibleFullRect))
                        canvas.DrawImage(tile.Image, tile.DestRect, sampling);
                }

                break;
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
}