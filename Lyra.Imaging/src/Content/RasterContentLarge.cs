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

    public bool IsResolutionIndependent => false;
    
    public long ByteSize => Bytes(PreviewImage) + (TileSource?.ByteSize ?? 0);

    internal static long Bytes(SKImage? image) => image is null || image.Handle == IntPtr.Zero
        ? 0
        : (long)image.Width * image.Height * Math.Max(1, image.ColorType.GetBytesPerPixel());

    public float FullWidth { get; }
    public float FullHeight { get; }

    public SKImage? PreviewImage { get; private set; }

    /// <summary>
    /// Set when <see cref="PreviewImage"/> holds scene-referred half-float light rather than
    /// display-ready pixels - the measured white point the tone mapper needs.
    /// </summary>
    public float? PreviewWhitePoint { get; private set; }

    public bool HasScenePreview => PreviewImage is not null && PreviewWhitePoint is not null;

    /// <summary>
    /// Set when the tiles hold scene-referred light as well, so the image keeps its headroom and
    /// controls at every zoom.
    /// </summary>
    public float? TileWhitePoint { get; private set; }

    public bool HasSceneTiles => TileSource is not null && TileWhitePoint is not null;

    public ITileSource? TileSource { get; private set; }
    
    public float? DecodedWidth => PreviewImage?.Width;

    public float? DecodedHeight => PreviewImage?.Height;

    public bool HasPreview => PreviewImage != null;
    public bool HasTiles => TileSource != null;

    private int _tilesReady;
    public int TilesReady => Volatile.Read(ref _tilesReady);
    public int? TilesTotal { get; private set; }

    public event Action<RasterLargeContent>? TilesProgressChanged;

    private long _lastProgressTicks;
    private const long ProgressThrottleMs = 33;

    public void SetTilesTotal(int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        TilesTotal = total;
        Interlocked.Exchange(ref _tilesReady, 0);
    }
    
    public void MarkAllTilesReady(int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);

        TilesTotal = total;
        Interlocked.Exchange(ref _tilesReady, total);

        TilesProgressChanged?.Invoke(this);
    }

    public void IncrementTileReady()
    {
        var ready = Interlocked.Increment(ref _tilesReady);
        var total = TilesTotal;

        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastProgressTicks);
        var isLast = total is { } t && ready >= t;

        if (isLast || now - last >= ProgressThrottleMs)
        {
            Interlocked.Exchange(ref _lastProgressTicks, now);
            TilesProgressChanged?.Invoke(this);
        }
    }

    /// <summary>
    /// Replaces the preview with scene-referred half-float light, to be tone-mapped at draw time.
    /// </summary>
    public void SetScenePreview(SKImage preview, float whitePoint)
    {
        ArgumentNullException.ThrowIfNull(preview);

        SetPreview(preview);
        PreviewWhitePoint = whitePoint;
    }

    public void SetPreview(SKImage? preview)
    {
        if (PreviewImage != null && PreviewImage.Handle != IntPtr.Zero)
            PreviewImage.Dispose();

        PreviewImage = preview;
    }

    /// <summary>
    /// Marks preview and tiles alike as scene-referred light, to be tone-mapped at draw time.
    /// </summary>
    public void MarkSceneReferred(float whitePoint)
    {
        PreviewWhitePoint = whitePoint;
        TileWhitePoint = whitePoint;
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

        TileSource?.Dispose();
    }
}

public interface ITileSource : IDisposable
{
    IEnumerable<RasterTile> GetTiles(SKRect visibleFullRect, SKSize imageSize);
    
    long ByteSize { get; }
}

public readonly record struct RasterTile(SKImage Image, SKRect DestRect);