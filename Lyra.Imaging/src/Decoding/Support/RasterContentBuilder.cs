using Lyra.Common;
using Lyra.Imaging.ConstraintsProvider;
using Lyra.Imaging.Content;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Wraps full-resolution decoded pixels as displayable content, choosing between a single image
/// and the preview-plus-tiles path that very large rasters need.
/// </summary>
internal static class RasterContentBuilder
{
    /// <summary>
    /// Above this, publish a preview and tiles instead of one texture.
    /// </summary>
    private const long SingleTextureByteBudget = 256L * 1024 * 1024;

    /// <summary>
    /// Tile edge in pixels. 2048 costs 16 MiB per tile, so the handful covering a screen stays
    /// far inside any sane cache, while keeping the tile count low enough that walking them per
    /// frame is free (a 16K image is 8x4).
    /// </summary>
    private const int TileEdge = 2048;

    /// <summary>
    /// Preview is sized to the display so it is sharp at fit-to-window, with headroom for a
    /// little zoom before tiles take over.
    /// </summary>
    private const float PreviewSizeMultiplier = 2.0f;

    /// <summary>
    /// Used when no display bounds have been published yet - decode can finish before the first
    /// <c>DisplayBoundsChangedEvent</c>, and a zero-sized preview would be worse than a guess.
    /// </summary>
    private const int FallbackDisplayEdge = 2560;
    
    public static ICompositeContent Build(SKBitmap bitmap, Composite composite, float? sceneWhitePoint = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(composite);

        bitmap.SetImmutable();

        var bytes = (long)bitmap.Width * bitmap.Height * Math.Max(1, bitmap.ColorType.GetBytesPerPixel());
        if (bytes <= SingleTextureByteBudget)
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
                    $"{bitmap.ColorType}, over the {SingleTextureByteBudget / 1024 / 1024} MB " +
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
        var display = DecodeConstraintsProvider.Current;

        var maxWidth = (int)((display.LogicalWidth > 0 ? display.LogicalWidth : FallbackDisplayEdge) * PreviewSizeMultiplier);
        var maxHeight = (int)((display.LogicalHeight > 0 ? display.LogicalHeight : FallbackDisplayEdge) * PreviewSizeMultiplier);

        var scale = MathF.Min(1f, MathF.Min(maxWidth / (float)width, maxHeight / (float)height));

        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }

    /// <summary>
    /// Cuts the image into tiles that share its pixels rather than copying them.
    /// </summary>
    /// <remarks>
    /// <see cref="SKBitmap.ExtractSubset"/> produces a view onto the same pixel storage;
    /// <c>SKImage.Subset</c> would copy.
    /// </remarks>
    private static ITileSource CreateTiles(SKBitmap bitmap, out int tileCount)
    {
        var tilesX = (bitmap.Width + TileEdge - 1) / TileEdge;
        var tilesY = (bitmap.Height + TileEdge - 1) / TileEdge;

        var tiles = new RasterTileSource(tilesX, tilesY, TileEdge, TileEdge);
        var views = new List<SKBitmap>(tilesX * tilesY);

        for (var y = 0; y < tilesY; y++)
        for (var x = 0; x < tilesX; x++)
        {
            // The right and bottom edges are short unless the image divides evenly.
            var rect = SKRectI.Create(
                x * TileEdge,
                y * TileEdge,
                Math.Min(TileEdge, bitmap.Width - x * TileEdge),
                Math.Min(TileEdge, bitmap.Height - y * TileEdge)
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
        public long ByteSize => source.ByteCount;

        public IEnumerable<RasterTile> GetTiles(SKRect visibleFullRect, SKSize imageSize) => tiles.GetTiles(visibleFullRect, imageSize);
        
        public long VisibleByteSize(SKRect visibleFullRect, SKSize imageSize) => tiles.VisibleByteSize(visibleFullRect, imageSize);

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