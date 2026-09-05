using SkiaSharp;

namespace Lyra.Imaging.Content;

public sealed class RasterTileSource : ITileSource
{
    private readonly int _tilesX;
    private readonly int _tilesY;
    private readonly float _tileW;
    private readonly float _tileH;

    private SKImage?[] _tiles; // fixed slots; swapped for an empty array on dispose
    private volatile bool _disposed;

    public RasterTileSource(int tilesX, int tilesY, float tileWidth, float tileHeight)
    {
        _tilesX = tilesX;
        _tilesY = tilesY;
        _tileW = tileWidth;
        _tileH = tileHeight;

        _tiles = new SKImage?[tilesX * tilesY];
    }

    public void SetTile(int x, int y, SKImage image)
    {
        if ((uint)x >= (uint)_tilesX || (uint)y >= (uint)_tilesY)
            throw new ArgumentOutOfRangeException($"Tile index {x},{y} out of range.");

        var tiles = Volatile.Read(ref _tiles);
        if (tiles.Length == 0)
        {
            image.Dispose();
            return;
        }

        var idx = y * _tilesX + x;

        // atomic swap; dispose old if replaced
        var old = Interlocked.Exchange(ref tiles[idx], image);
        old?.Dispose();

        if (_disposed)
        {
            var mine = Interlocked.Exchange(ref tiles[idx], null);
            mine?.Dispose();
        }
    }

    /// <summary>Sum of the tiles decoded so far; slots not yet filled cost nothing.</summary>
    public long ByteSize
    {
        get
        {
            var tiles = Volatile.Read(ref _tiles);
            var total = 0L;

            for (var i = 0; i < tiles.Length; i++)
                total += RasterLargeContent.Bytes(Volatile.Read(ref tiles[i]));

            return total;
        }
    }

    public long VisibleByteSize(SKRect visibleFullRect, SKSize imageSize)
    {
        var total = 0L;
        foreach (var tile in GetTiles(visibleFullRect, imageSize))
            total += RasterLargeContent.Bytes(tile.Image);

        return total;
    }

    public IEnumerable<RasterTile> GetTiles(SKRect visibleFullRect, SKSize imageSize)
    {
        if (visibleFullRect.IsEmpty)
            yield break;

        var tiles = Volatile.Read(ref _tiles);
        if (tiles.Length == 0)
            yield break; // disposed

        // Compute index range that overlaps the visible rect
        var minX = Math.Clamp((int)MathF.Floor(visibleFullRect.Left / _tileW), 0, _tilesX - 1);
        var maxX = Math.Clamp((int)MathF.Floor((visibleFullRect.Right - 1) / _tileW), 0, _tilesX - 1);

        var minY = Math.Clamp((int)MathF.Floor(visibleFullRect.Top / _tileH), 0, _tilesY - 1);
        var maxY = Math.Clamp((int)MathF.Floor((visibleFullRect.Bottom - 1) / _tileH), 0, _tilesY - 1);

        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var idx = y * _tilesX + x;
            var img = Volatile.Read(ref tiles[idx]);
            if (img == null) 
                continue;

            var left = x * _tileW;
            var top  = y * _tileH;

            var w = Math.Min(_tileW, imageSize.Width - left);
            var h = Math.Min(_tileH, imageSize.Height - top);
            
            var dest = SKRect.Create(left, top, w, h);

            if (!dest.IntersectsWith(visibleFullRect)) 
                continue;

            yield return new RasterTile(img, dest);
        }
    }

    public void Dispose()
    {
        _disposed = true;

        var tiles = Interlocked.Exchange(ref _tiles, []);
        for (var i = 0; i < tiles.Length; i++)
        {
            var t = Interlocked.Exchange(ref tiles[i], null);
            t?.Dispose();
        }
    }
}