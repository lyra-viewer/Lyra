using SkiaSharp;

namespace Lyra.Imaging.Content;

public sealed class RasterTileSource : ITileSource
{
    private readonly int _tilesX;
    private readonly int _tilesY;
    private readonly float _tileW;
    private readonly float _tileH;

    private SKImage?[] _tiles; // fixed slots

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

        var idx = y * _tilesX + x;

        // atomic swap; dispose old if replaced
        var old = Interlocked.Exchange(ref _tiles[idx], image);
        old?.Dispose();
    }

    public IEnumerable<RasterTile> GetTiles(SKRect visibleFullRect, SKSize imageSize)
    {
        if (visibleFullRect.IsEmpty)
            yield break;
        
        // Compute index range that overlaps the visible rect
        var minX = Math.Clamp((int)MathF.Floor(visibleFullRect.Left / _tileW), 0, _tilesX - 1);
        var maxX = Math.Clamp((int)MathF.Floor((visibleFullRect.Right - 1) / _tileW), 0, _tilesX - 1);

        var minY = Math.Clamp((int)MathF.Floor(visibleFullRect.Top / _tileH), 0, _tilesY - 1);
        var maxY = Math.Clamp((int)MathF.Floor((visibleFullRect.Bottom - 1) / _tileH), 0, _tilesY - 1);

        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var idx = y * _tilesX + x;
            var img = Volatile.Read(ref _tiles[idx]);
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
        var tiles = Interlocked.Exchange(ref _tiles, Array.Empty<SKImage?>());
        foreach (var t in tiles)
            t?.Dispose();
    }
}