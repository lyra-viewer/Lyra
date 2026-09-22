using Lyra.Common;
using Lyra.Imaging.ConstraintsProvider;
using Lyra.Imaging.Content.Tiling;
using SkiaSharp;
using Lyra.Imaging.Content;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Wraps full-resolution decoded pixels as displayable content, choosing between a single image
/// and the preview-plus-tiles path that very large rasters need.
/// </summary>
internal static class RasterContentBuilder
{
    public static ICompositeContent Build(SKBitmap bitmap, Composite composite, float? sceneWhitePoint = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(composite);

        bitmap.SetImmutable();

        var bytes = (long)bitmap.Width * bitmap.Height * Math.Max(1, bitmap.ColorType.GetBytesPerPixel());
        if (bytes <= DecodePolicy.SingleTextureCeilingBytes)
            return Single(bitmap, sceneWhitePoint);

        try
        {
            var large = BuildLarge(bitmap, composite);

            if (sceneWhitePoint is { } whitePoint)
                large.MarkSceneReferred(whitePoint);

            return large;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[RasterContentBuilder] Could not build the tiled form ({ex.Message}); falling back to a single texture.");
            return Single(bitmap, sceneWhitePoint);
        }
    }

    /// <summary>One texture for the whole image, scene-referred when a white point came with it.</summary>
    private static RasterContent Single(SKBitmap bitmap, float? sceneWhitePoint)
    {
        var image = SKImage.FromBitmap(bitmap);

        return sceneWhitePoint is { } whitePoint
            ? new HdrRasterContent(bitmap, image, whitePoint)
            : new RasterContent(bitmap, image);
    }

    private static RasterLargeContent BuildLarge(SKBitmap bitmap, Composite composite)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;

        var content = new RasterLargeContent(width, height);

        composite.FullWidth = width;
        composite.FullHeight = height;

        try
        {
            content.SetPreview(CreatePreview(bitmap));
            content.SetTiles(CreateTiles(bitmap, out var tileCount));

            content.MarkAllTilesReady(tileCount);
        }
        catch
        {
            content.SetPreview(null);
            throw;
        }

        var bytes = (long)width * height * Math.Max(1, bitmap.ColorType.GetBytesPerPixel());

        Logger.Info($"[RasterContentBuilder] {width}x{height} is {bytes / 1024 / 1024} MB as " +
                    $"{bitmap.ColorType}, over the {DecodePolicy.SingleTextureCeilingBytes / 1024 / 1024} MB " +
                    "single-texture budget; publishing a preview plus tiles so the GPU only holds " +
                    "what is on screen.");

        return content;
    }

    /// <summary>
    /// A display-sized copy, drawn whenever it is sharp enough for the current zoom.
    /// </summary>
    private static SKImage? CreatePreview(SKBitmap bitmap)
    {
        var (targetWidth, targetHeight) = PreviewSize(bitmap.Width, bitmap.Height);

        var info = new SKImageInfo(targetWidth, targetHeight, bitmap.ColorType, bitmap.AlphaType, bitmap.ColorSpace);
        var preview = bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));

        if (preview is null)
        {
            Logger.Warning($"[RasterContentBuilder] Preview resize to {targetWidth}x{targetHeight} failed; tiles will carry the image alone.");
            return null;
        }

        preview.SetImmutable();
        return SKImage.FromBitmap(preview);
    }

    internal static (int Width, int Height) PreviewSize(int width, int height)
    {
        var (maxWidth, maxHeight) = PreviewBounds();

        var scale = MathF.Min(1f, MathF.Min(maxWidth / (float)width, maxHeight / (float)height));

        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }

    /// <summary>
    /// The largest a preview may be, for the display currently published. Every path that builds
    /// one asks here, so none of them has to remember that the snapshot can still be empty.
    /// </summary>
    internal static (int Width, int Height) PreviewBounds() =>
        PreviewBounds(DecodeConstraintsProvider.Current.LogicalWidth, DecodeConstraintsProvider.Current.LogicalHeight);

    /// <param name="logicalWidth">Display width, or zero when no bounds have been published yet.</param>
    /// <param name="logicalHeight">Display height, or zero when no bounds have been published yet.</param>
    internal static (int Width, int Height) PreviewBounds(int logicalWidth, int logicalHeight) =>
        (
            (int)((logicalWidth > 0 ? logicalWidth : DecodePolicy.FallbackDisplayEdge) * DecodePolicy.PreviewSizeMultiplier),
            (int)((logicalHeight > 0 ? logicalHeight : DecodePolicy.FallbackDisplayEdge) * DecodePolicy.PreviewSizeMultiplier)
        );

    /// <summary>
    /// Cuts the image into tiles that share its pixels rather than copying them.
    /// </summary>
    /// <remarks>
    /// <see cref="SKBitmap.ExtractSubset"/> produces a view onto the same pixel storage;
    /// <c>SKImage.Subset</c> would copy.
    /// </remarks>
    private static ITileSource CreateTiles(SKBitmap bitmap, out int tileCount)
    {
        var tilesX = (bitmap.Width + DecodePolicy.TileEdge - 1) / DecodePolicy.TileEdge;
        var tilesY = (bitmap.Height + DecodePolicy.TileEdge - 1) / DecodePolicy.TileEdge;

        var tiles = new RasterTileSource(tilesX, tilesY, DecodePolicy.TileEdge, DecodePolicy.TileEdge);
        var views = new List<SKBitmap>(tilesX * tilesY);

        for (var y = 0; y < tilesY; y++)
        for (var x = 0; x < tilesX; x++)
        {
            // The right and bottom edges are short unless the image divides evenly.
            var rect = SKRectI.Create(
                x * DecodePolicy.TileEdge,
                y * DecodePolicy.TileEdge,
                Math.Min(DecodePolicy.TileEdge, bitmap.Width - x * DecodePolicy.TileEdge),
                Math.Min(DecodePolicy.TileEdge, bitmap.Height - y * DecodePolicy.TileEdge)
            );

            var view = new SKBitmap();
            if (!bitmap.ExtractSubset(view, rect))
            {
                view.Dispose();
                continue;
            }

            view.SetImmutable();
            views.Add(view);
            tiles.SetTile(x, y, SKImage.FromBitmap(view));
        }
        
        tileCount = views.Count;

        if (tileCount != tilesX * tilesY)
            Logger.Warning($"[RasterContentBuilder] Only {tileCount} of {tilesX * tilesY} tiles could be extracted; the image will have gaps at full zoom.");

        return new SharedPixelTileSource(tiles, bitmap, views);
    }
    
    private sealed class SharedPixelTileSource(RasterTileSource tiles, SKBitmap source, List<SKBitmap> views)
        : ITileSource
    {
        public long ByteSize => (long)source.RowBytes * source.Height;

        public IEnumerable<RasterTile> GetTiles(SKRect visibleFullRect, SKSize imageSize, float pixelsPerFullUnit) =>
            tiles.GetTiles(visibleFullRect, imageSize, pixelsPerFullUnit);
        
        public long VisibleByteSize(SKRect visibleFullRect, SKSize imageSize, float pixelsPerFullUnit) =>
            tiles.VisibleByteSize(visibleFullRect, imageSize, pixelsPerFullUnit);

        public void Dispose()
        {
            tiles.Dispose();

            foreach (var view in views)
                view.Dispose();

            views.Clear();
            source.Dispose();
        }
    }
}