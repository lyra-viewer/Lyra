using Lyra.Imaging.Content.Hdr;
using Lyra.Imaging.Content.Tiling;
using SkiaSharp;

namespace Lyra.Imaging.Content;

/// <summary>
/// A raster too large for one texture: a preview, plus tiles drawn where the view is close enough.
/// Built before it is published, and <see cref="Seal">sealed</see> then: from that point only
/// the tiles' progress changes.
/// </summary>
public sealed class RasterLargeContent : ICompositeContent
{
    public RasterLargeContent(float fullWidth, float fullHeight, SKImage? previewImage = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fullWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fullHeight);

        FullWidth = fullWidth;
        FullHeight = fullHeight;

        if (previewImage is not null)
            SetPreview(previewImage);
    }

    public bool IsResolutionIndependent => false;

    /// <summary>The preview's cost and size, read when it is set rather than from the image later.</summary>
    private sealed record PreviewFacts(int Width, int Height, long Bytes);

    private volatile PreviewFacts? _previewFacts;

    public long ByteSize => IsDisposed ? 0 : (_previewFacts?.Bytes ?? 0) + (TileSource?.ByteSize ?? 0);

    public float FullWidth { get; }
    public float FullHeight { get; }

    public SKImage? PreviewImage { get; private set; }

    /// <summary>
    /// Set when <see cref="PreviewImage"/> holds scene-referred half-float light rather than
    /// display-ready pixels - the measured white point the tone mapper needs.
    /// </summary>
    public float? PreviewWhitePoint { get; private set; }

    public SceneEncoding PreviewEncoding { get; private set; }

    public bool HasScenePreview => PreviewImage is not null && PreviewWhitePoint is not null;

    /// <summary>
    /// Set when the tiles hold scene-referred light as well, so the image keeps its headroom and
    /// controls at every zoom.
    /// </summary>
    public float? TileWhitePoint { get; private set; }

    public SceneEncoding TileEncoding { get; private set; }

    public ITileSource? TileSource { get; private set; }

    private SceneRange _range;

    /// <summary>Where the light ends, when the format knows; set before the content is published.</summary>
    public SceneRange Range
    {
        get => _range;
        internal set
        {
            ThrowIfSealed();
            _range = value;
        }
    }

    public float? DecodedWidth => _previewFacts?.Width;

    public float? DecodedHeight => _previewFacts?.Height;

    public bool HasPreview => PreviewImage != null;
    public bool HasTiles => TileSource != null;

    private int _tilesReady;
    public int TilesReady => Volatile.Read(ref _tilesReady);
    public int? TilesTotal { get; private set; }

    public event Action<RasterLargeContent>? TilesProgressChanged;

    private long _lastProgressTicks;
    private const long ProgressThrottleMs = 33;

    #region Building, before the content is published

    private volatile bool _sealed;

    /// <summary>Whether the content has been published, after which only tile progress changes.</summary>
    public bool IsSealed => _sealed;

    /// <summary>Ends building. Called when the content is published.</summary>
    internal void Seal() => _sealed = true;

    private void ThrowIfSealed()
    {
        if (_sealed)
            throw new InvalidOperationException("A large raster cannot be changed once it is published.");
    }

    internal void SetTilesTotal(int total)
    {
        ThrowIfSealed();
        ArgumentOutOfRangeException.ThrowIfNegative(total);

        TilesTotal = total;
        Interlocked.Exchange(ref _tilesReady, 0);
    }

    internal void MarkAllTilesReady(int total)
    {
        ThrowIfSealed();
        ArgumentOutOfRangeException.ThrowIfNegative(total);

        TilesTotal = total;
        Interlocked.Exchange(ref _tilesReady, total);

        TilesProgressChanged?.Invoke(this);
    }

    /// <summary>
    /// Replaces the preview with scene-referred half-float light, to be tone-mapped at draw time.
    /// </summary>
    internal void SetScenePreview(SKImage preview, float whitePoint)
    {
        ArgumentNullException.ThrowIfNull(preview);

        SetPreview(preview);
        PreviewWhitePoint = whitePoint;
        PreviewEncoding = SceneEncoding.Linear;
    }

    internal void SetPreview(SKImage? preview)
    {
        ThrowIfSealed();

        var previous = PreviewImage;

        PreviewImage = preview;
        _previewFacts = preview is null ? null : new PreviewFacts(preview.Width, preview.Height, ImageBytes.Of(preview));

        previous?.Dispose();
    }

    /// <summary>
    /// Marks preview and tiles alike as scene-referred light, to be tone-mapped at draw time.
    /// </summary>
    internal void MarkSceneReferred(float whitePoint, SceneEncoding encoding = SceneEncoding.Linear)
    {
        ThrowIfSealed();

        PreviewWhitePoint = whitePoint;
        TileWhitePoint = whitePoint;
        PreviewEncoding = encoding;
        TileEncoding = encoding;
    }

    internal void SetTiles(ITileSource tiles)
    {
        ThrowIfSealed();
        ArgumentNullException.ThrowIfNull(tiles);

        TileSource?.Dispose();
        TileSource = tiles;
    }

    #endregion

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

    private int _disposed;

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        PreviewImage?.Dispose();
        TileSource?.Dispose();
        _range.Curve?.Dispose();
    }
}
