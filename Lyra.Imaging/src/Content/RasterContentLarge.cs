using SkiaSharp;

namespace Lyra.Imaging.Content;

public sealed class RasterLargeContent : ICompositeContent
{
    public RasterLargeContent(float fullWidth, float fullHeight, SKImage? previewImage = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fullWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fullHeight);

        FullWidth = fullWidth;
        FullHeight = fullHeight;
        PreviewImage = previewImage;
    }

    public CompositeContentKind Kind => CompositeContentKind.RasterLarge;

    public float FullWidth { get; }
    public float FullHeight { get; }

    public SKImage? PreviewImage { get; private set; }
    public SKImage? FullImage { get; private set; }
    public ITileSource? TileSource { get; private set; }

    public float? DecodedWidth => FullImage?.Width ?? PreviewImage?.Width;
    public float? DecodedHeight => FullImage?.Height ?? PreviewImage?.Height;

    public bool HasPreview => PreviewImage != null;
    public bool HasFullImage => FullImage != null;
    public bool HasTiles => TileSource != null;

    private int _tilesReady;
    public int TilesReady => Volatile.Read(ref _tilesReady);
    public int? TilesTotal { get; private set; }

    public void SetTilesTotal(int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        TilesTotal = total;
        Interlocked.Exchange(ref _tilesReady, 0);
    }

    public void IncrementTileReady()
    {
        Interlocked.Increment(ref _tilesReady);
    }


    public void SetPreview(SKImage? preview)
    {
        if (PreviewImage != null && PreviewImage.Handle != IntPtr.Zero)
            PreviewImage.Dispose();

        PreviewImage = preview;
    }

    public void SetFullImage(SKImage full)
    {
        if (FullImage != null && FullImage.Handle != IntPtr.Zero)
            FullImage.Dispose();

        FullImage = full ?? throw new ArgumentNullException(nameof(full));
    }

    public void SetTiles(ITileSource tiles)
    {
        TileSource?.Dispose();
        TileSource = tiles ?? throw new ArgumentNullException(nameof(tiles));
    }

    public void Dispose()
    {
        if (PreviewImage != null && PreviewImage.Handle != IntPtr.Zero)
            PreviewImage.Dispose();

        if (FullImage != null && FullImage.Handle != IntPtr.Zero)
            FullImage.Dispose();

        TileSource?.Dispose();
    }
}

public interface ITileSource : IDisposable
{
    IEnumerable<RasterTile> GetTiles(SKRect visibleFullRect, SKSize imageSize);
}

public readonly record struct RasterTile(SKImage Image, SKRect DestRect);