using Lyra.Imaging.Psd.Core.Decode.Pixel;

namespace Lyra.Imaging.Psd.Core.Decode.Composite;

public sealed class TiledCompositeImage : ICompositeImage
{
    public int Width { get; }
    public int Height { get; }
    public SurfaceFormat Format { get; }

    public int TileWidth { get; }
    public int TileHeight { get; }
    public int TilesX { get; }
    public int TilesY { get; }
    public int TileCount => TilesX * TilesY;

    // Storage: index -> tile (null means not decoded / not present yet)
    private readonly RgbaSurface?[] _tiles;

    public TiledCompositeImage(int width, int height, SurfaceFormat format, int tileWidth, int tileHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileHeight);

        Width = width;
        Height = height;
        Format = format;
        TileWidth = tileWidth;
        TileHeight = tileHeight;

        TilesX = (width + tileWidth - 1) / tileWidth;
        TilesY = (height + tileHeight - 1) / tileHeight;

        _tiles = new RgbaSurface?[TilesX * TilesY];
    }

    public TileInfo GetTileInfo(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)TilesX)
            throw new ArgumentOutOfRangeException(nameof(tileX));

        if ((uint)tileY >= (uint)TilesY)
            throw new ArgumentOutOfRangeException(nameof(tileY));

        var x = tileX * TileWidth;
        var y = tileY * TileHeight;

        var w = Math.Min(TileWidth, Width - x);
        var h = Math.Min(TileHeight, Height - y);

        var index = tileY * TilesX + tileX;
        return new TileInfo(x, y, w, h, index);
    }

    public RgbaSurface? TryGetTile(int tileX, int tileY) => _tiles[GetTileInfo(tileX, tileY).Index];

    public void SetTile(int tileX, int tileY, RgbaSurface tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        var info = GetTileInfo(tileX, tileY);

        // Validate that the provided surface matches the expected tile dimensions
        if (tile.Width != info.Width || tile.Height != info.Height)
        {
            throw new InvalidOperationException($"Tile size mismatch for ({tileX},{tileY}). Expected {info.Width}x{info.Height}, got {tile.Width}x{tile.Height}.");
        }

        // Overwrite policy: dispose old tile if overwriting
        _tiles[info.Index]?.Dispose();
        _tiles[info.Index] = tile;
    }

    public ReadOnlySpan<RgbaSurface?> Tiles => _tiles;

    public void Dispose()
    {
        foreach (var t in _tiles)
            t?.Dispose();
    }
}