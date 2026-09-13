using Lyra.Common;
using SkiaSharp;

namespace Lyra.Imaging.Content.Tiling;

/// <summary>
/// A tile grid whose tiles are decoded when the view asks for them and dropped when it stops.
/// </summary>
public sealed class LazyTileSource : ITileSource
{
    private readonly int _tilesX;
    private readonly int _tilesY;
    private readonly float _tileW;
    private readonly float _tileH;

    /// <summary>Coarsest level available; 0 means full resolution only.</summary>
    private readonly int _maxLevel;

    private readonly ITileProvider _provider;
    private readonly long _residentByteBudget;

    /// <summary>One tile of one level. Levels coexist, so the key has to carry it.</summary>
    private readonly record struct Key(int Level, int X, int Y);

    private readonly Dictionary<Key, SKImage> _tiles = [];

    /// <summary>Least recently wanted first.</summary>
    private readonly List<Key> _recent = [];

    /// <summary>Tiles the current view covers. Nothing here is evicted, and nothing else is fetched.</summary>
    private readonly HashSet<Key> _wanted = [];

    private readonly Queue<Key> _queue = new();
    private readonly HashSet<Key> _queued = [];

    private readonly Lock _gate = new();
    private CancellationTokenSource? _cancellation = new();
    private Task? _worker;
    
    private volatile bool _disposed;

    private int _providerDisposed;

    private readonly int _bytesPerPixel;

    /// <param name="bytesPerPixel">
    /// What one decoded pixel costs, so the renderer can be told what a viewport <em>would</em>
    /// cost before any of it exists.
    /// </param>
    /// <param name="maxLevel">
    /// How many halving of resolution the provider can produce. Without them there is nothing
    /// between the preview and full resolution, and the zoom band in between draws full-resolution
    /// tiles it has to shrink - which is both the slowest thing to fetch and the most to hold.
    /// </param>
    public LazyTileSource(int tilesX, int tilesY, float tileWidth, float tileHeight, ITileProvider provider, long residentByteBudget, int bytesPerPixel = 4, int maxLevel = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tilesX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tilesY);
        ArgumentNullException.ThrowIfNull(provider);

        _tilesX = tilesX;
        _tilesY = tilesY;
        _tileW = tileWidth;
        _tileH = tileHeight;
        _provider = provider;
        _residentByteBudget = Math.Max(0, residentByteBudget);
        _bytesPerPixel = Math.Max(1, bytesPerPixel);
        _maxLevel = Math.Max(0, maxLevel);
    }

    /// <summary>Raised on a background thread when a tile becomes drawable.</summary>
    public event Action<LazyTileSource>? TileReady;

    public int TileCount => _tilesX * _tilesY;

    /// <summary>
    /// The level to draw at this scale: the coarsest one whose texels are still at least as dense
    /// as the screen needs, so nothing is ever magnified and the texture drawn is about the size
    /// of the area it lands on.
    /// </summary>
    internal int LevelFor(float pixelsPerFullUnit)
    {
        if (_maxLevel == 0 || pixelsPerFullUnit <= 0 || !float.IsFinite(pixelsPerFullUnit))
            return 0;

        if (pixelsPerFullUnit >= 1f)
            return 0;

        var level = (int)MathF.Floor(-MathF.Log2(pixelsPerFullUnit));
        return Math.Clamp(level, 0, _maxLevel);
    }

    private (int X, int Y) GridAt(int level) =>
        (Math.Max(1, (_tilesX + (1 << level) - 1) >> level), Math.Max(1, (_tilesY + (1 << level) - 1) >> level));

    public int ResidentCount
    {
        get
        {
            lock (_gate)
                return _tiles.Count;
        }
    }

    public long ByteSize
    {
        get
        {
            lock (_gate)
                return _tiles.Values.Sum(RasterLargeContent.Bytes);
        }
    }

    /// <summary>
    /// What the tiles this rect covers would cost as textures, decoded or not.
    /// </summary>
    public long VisibleByteSize(SKRect visibleFullRect, SKSize imageSize, float pixelsPerFullUnit)
    {
        var perTile = (long)_tileW * (long)_tileH * _bytesPerPixel;
        var count = 0L;

        foreach (var _ in Overlapping(visibleFullRect, LevelFor(pixelsPerFullUnit)))
            count++;

        return count * perTile;
    }

    public IEnumerable<RasterTile> GetTiles(SKRect visibleFullRect, SKSize imageSize, float pixelsPerFullUnit)
    {
        if (visibleFullRect.IsEmpty)
            return [];

        var level = LevelFor(pixelsPerFullUnit);

        List<RasterTile> ready;
        var request = false;

        lock (_gate)
        {
            if (_disposed)
                return [];

            _wanted.Clear();
            ready = [];

            foreach (var key in Overlapping(visibleFullRect, level))
            {
                _wanted.Add(key);

                if (_tiles.TryGetValue(key, out var image))
                {
                    Touch(key);
                    ready.Add(new RasterTile(image, DestinationOf(key, imageSize)));
                }
                else if (_queued.Add(key))
                {
                    _queue.Enqueue(key);
                    request = true;
                }
            }
            
            if (ready.Count == 0 && level < _maxLevel)
                ready = Fallback(visibleFullRect, level, imageSize);

            if (request)
                EnsureWorker();
        }

        return ready;
    }

    /// <summary>
    /// The coarsest-but-nearest level that is already decoded for this rect. Call under the lock.
    /// </summary>
    private List<RasterTile> Fallback(SKRect rect, int level, SKSize imageSize)
    {
        for (var coarser = level + 1; coarser <= _maxLevel; coarser++)
        {
            List<RasterTile> found = [];
            List<Key> keys = [];

            foreach (var key in Overlapping(rect, coarser))
                if (_tiles.TryGetValue(key, out var image))
                {
                    found.Add(new RasterTile(image, DestinationOf(key, imageSize)));
                    keys.Add(key);
                }

            if (found.Count == 0)
                continue;
            
            foreach (var key in keys)
            {
                _wanted.Add(key);
                Touch(key);
            }

            return found;
        }

        return [];
    }

    /// <summary>Tile keys overlapping a rect in full-image coordinates, at one level.</summary>
    private IEnumerable<Key> Overlapping(SKRect rect, int level)
    {
        var (gridX, gridY) = GridAt(level);

        var spanW = _tileW * (1 << level);
        var spanH = _tileH * (1 << level);

        var minX = Math.Clamp((int)MathF.Floor(rect.Left / spanW), 0, gridX - 1);
        var maxX = Math.Clamp((int)MathF.Floor((rect.Right - 1) / spanW), 0, gridX - 1);
        var minY = Math.Clamp((int)MathF.Floor(rect.Top / spanH), 0, gridY - 1);
        var maxY = Math.Clamp((int)MathF.Floor((rect.Bottom - 1) / spanH), 0, gridY - 1);

        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
            yield return new Key(level, x, y);
    }

    private SKRect DestinationOf(Key key, SKSize imageSize)
    {
        var spanW = _tileW * (1 << key.Level);
        var spanH = _tileH * (1 << key.Level);

        var left = key.X * spanW;
        var top = key.Y * spanH;

        // Always full-image coordinates, whatever level the pixels came from - the drawer scales
        // the texture into this rect and never has to know which one it got.
        return SKRect.Create(left, top,
            Math.Min(spanW, imageSize.Width - left),
            Math.Min(spanH, imageSize.Height - top)
        );
    }

    private void Touch(Key key)
    {
        _recent.Remove(key);
        _recent.Add(key);
    }

    /// <summary>
    /// Halves the four tiles below <paramref name="key"/> into one, or returns null if they are
    /// not all resident at full size.
    /// </summary>
    private SKImage? BuildFromFiner(Key key)
    {
        if (key.Level == 0)
            return null;

        var edge = (int)_tileW;
        var half = edge / 2;

        if (edge <= 1 || half * 2 != edge)
            return null;

        // Snapshot under the lock; the pixels themselves are immutable once published.
        var children = new SKImage?[4];

        lock (_gate)
        {
            for (var i = 0; i < 4; i++)
            {
                var child = new Key(key.Level - 1, key.X * 2 + (i & 1), key.Y * 2 + (i >> 1));

                if (!_tiles.TryGetValue(child, out var image) || image.Width != edge || image.Height != edge)
                    return null;

                children[i] = image;
            }
        }

        var info = new SKImageInfo(edge, edge, SKColorType.Gray8, SKAlphaType.Opaque);
        var bitmap = new SKBitmap(info);

        try
        {
            unsafe
            {
                var dst = (byte*)bitmap.GetPixels();
                var dstStride = bitmap.RowBytes;

                for (var i = 0; i < 4; i++)
                {
                    using var pixmap = children[i]!.PeekPixels();
                    if (pixmap is null || pixmap.ColorType != SKColorType.Gray8)
                    {
                        bitmap.Dispose();
                        return null;
                    }

                    var src = (byte*)pixmap.GetPixels();
                    var srcStride = pixmap.RowBytes;

                    // Where this quadrant lands in the parent.
                    var offsetX = (i & 1) * half;
                    var offsetY = (i >> 1) * half;

                    for (var y = 0; y < half; y++)
                    {
                        var a = src + (long)(y * 2) * srcStride;
                        var b = a + srcStride;
                        var row = dst + (long)(offsetY + y) * dstStride + offsetX;
                        
                        for (var x = 0; x < half; x++)
                        {
                            var s = x * 2;
                            row[x] = (byte)((a[s] + a[s + 1] + b[s] + b[s + 1] + 2) >> 2);
                        }
                    }
                }
            }

            bitmap.SetImmutable();
            return SKImage.FromBitmap(bitmap);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private void EnsureWorker()
    {
        if (_worker is { IsCompleted: false } || _cancellation is null)
            return;

        var token = _cancellation.Token;
        _worker = Task.Run(() => Work(token), CancellationToken.None);
    }

    private void Work(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Key key;

            lock (_gate)
            {
                while (_queue.Count > 0 && !_wanted.Contains(_queue.Peek()))
                    _queued.Remove(_queue.Dequeue());

                if (_queue.Count == 0 || _disposed)
                {
                    _worker = null;
                    ReleaseProviderIfDisposed();
                    return;
                }

                key = _queue.Dequeue();
                _queued.Remove(key);
            }

            SKImage? image = null;

            try
            {
                image = BuildFromFiner(key) ?? _provider.Decode(key.Level, key.X, key.Y, ct);
            }
            catch (OperationCanceledException)
            {
                ReleaseProviderIfDisposed();
                return;
            }
            catch (Exception ex)
            {
                Logger.Warning($"[LazyTileSource] Tile {key.X},{key.Y} of level {key.Level} failed to decode: {ex.Message}");
            }

            if (image is null)
                continue;

            var publish = false;

            lock (_gate)
            {
                if (_disposed || ct.IsCancellationRequested)
                {
                    image.Dispose();
                    ReleaseProviderIfDisposed();
                    return;
                }

                if (_tiles.TryAdd(key, image))
                {
                    Touch(key);
                    Trim();
                    publish = true;
                }
                else
                {
                    image.Dispose(); // raced with another pass; keep the one already in place
                }
            }

            if (publish)
                TileReady?.Invoke(this);
        }

        ReleaseProviderIfDisposed();
    }

    /// <summary>
    /// Disposes the provider once the source is disposed and no decode is inside it.
    /// </summary>
    private void ReleaseProviderIfDisposed()
    {
        if (!_disposed)
            return;

        if (Interlocked.Exchange(ref _providerDisposed, 1) == 0)
            _provider.Dispose();
    }

    /// <summary>
    /// Drops the least recently wanted tiles until the resident total is inside the budget. Tiles
    /// the view currently covers are exempt, so a budget too small for one screenful still draws.
    /// Call under the lock.
    /// </summary>
    private void Trim()
    {
        var resident = _tiles.Values.Sum(RasterLargeContent.Bytes);

        for (var i = 0; i < _recent.Count && resident > _residentByteBudget; i++)
        {
            var key = _recent[i];

            if (_wanted.Contains(key) || !_tiles.TryGetValue(key, out var image))
                continue;

            resident -= RasterLargeContent.Bytes(image);

            image.Dispose();
            _tiles.Remove(key);
            _recent.RemoveAt(i);
            i--;
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        bool hadWorker;

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;

            hadWorker = _worker is { IsCompleted: false };
            cancellation = _cancellation;
            _cancellation = null;

            foreach (var image in _tiles.Values)
                image.Dispose();

            _tiles.Clear();
            _recent.Clear();
            _wanted.Clear();
            _queue.Clear();
            _queued.Clear();
        }

        cancellation?.Cancel();
        cancellation?.Dispose();

        if (!hadWorker)
            ReleaseProviderIfDisposed();
    }
}