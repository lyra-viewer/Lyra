using Lyra.Imaging.Psd.Core.Decode.Composite;
using Lyra.Imaging.Psd.Core.Decode.Decompressors;

namespace Lyra.Imaging.Psd.Core.Decode.Pixel;

/// <summary>
/// Row sink that collects planar rows into per-tile <see cref="PlaneImage"/> instances.
/// Intended as the bridge step for tiled decode: decompress full-width plane rows once,
/// but store them in tile-sized plane buffers, so existing ColorModeProcessors can be reused
/// tile-by-tile (PlaneImage -> RgbaSurface).
///
/// Assumes decompressor emits rows in plane-major order:
///   plane 0 rows y=0..H-1, then plane 1 rows y=0..H-1, etc.
/// </summary>
public sealed class TilePlaneImageRowSink : IPlaneRowConsumer
{
    private readonly int _imageWidth;
    private readonly int _imageHeight;

    private readonly int _tileWidth;
    private readonly int _tileHeight;

    private readonly int _tilesX;
    private readonly int _tilesY;

    private readonly int _bytesPerChannel;
    private readonly PlaneRole[] _roles;

    private readonly TileInfo[] _tileInfos;
    private readonly PlaneImage?[] _tilePlanes;

    private readonly Action<int, int, PlaneImage>? _onTileCompleted;

    // Ordering validation (region-aware).
    private int _lastPlane = -1;
    private int _lastY = -1;
    private int _passYStart;
    private int _passYEnd;

    public TilePlaneImageRowSink(int imageWidth, int imageHeight, int tileWidth, int tileHeight, int bitsPerChannel, PlaneRole[] roles, Action<int, int, PlaneImage>? onTileCompleted = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileHeight);
        ArgumentNullException.ThrowIfNull(roles);

        _bytesPerChannel = bitsPerChannel switch
        {
            8 => 1,
            16 => 2,
            32 => 4,
            _ => throw new NotSupportedException($"TilePlaneImageRowSink: bitsPerChannel {bitsPerChannel} not supported (expected 8/16/32).")
        };

        _imageWidth = imageWidth;
        _imageHeight = imageHeight;

        _tileWidth = tileWidth;
        _tileHeight = tileHeight;

        _tilesX = (imageWidth + tileWidth - 1) / tileWidth;
        _tilesY = (imageHeight + tileHeight - 1) / tileHeight;

        _roles = roles;
        _onTileCompleted = onTileCompleted;

        _tileInfos = new TileInfo[_tilesX * _tilesY];
        for (var ty = 0; ty < _tilesY; ty++)
        {
            for (var tx = 0; tx < _tilesX; tx++)
            {
                var x = tx * _tileWidth;
                var y = ty * _tileHeight;

                var w = Math.Min(_tileWidth, _imageWidth - x);
                var h = Math.Min(_tileHeight, _imageHeight - y);

                var index = ty * _tilesX + tx;
                _tileInfos[index] = new TileInfo(x, y, w, h, index);
            }
        }

        _tilePlanes = new PlaneImage?[_tileInfos.Length];
    }

    public int TilesX => _tilesX;
    public int TilesY => _tilesY;
    public int TileCount => _tileInfos.Length;

    public TileInfo GetTileInfo(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)_tilesX)
            throw new ArgumentOutOfRangeException(nameof(tileX));
        if ((uint)tileY >= (uint)_tilesY)
            throw new ArgumentOutOfRangeException(nameof(tileY));

        return _tileInfos[tileY * _tilesX + tileX];
    }

    public PlaneImage? TryGetTilePlanes(int tileX, int tileY)
        => _tilePlanes[GetTileInfo(tileX, tileY).Index];

    /// <summary>
    /// Resets ordering validation for a new decompressor pass over a row region.
    /// </summary>
    public void BeginPass(int yStart, int yEndExclusive)
    {
        if ((uint)yStart >= (uint)_imageHeight)
            throw new ArgumentOutOfRangeException(nameof(yStart));
        if (yEndExclusive <= yStart || yEndExclusive > _imageHeight)
            throw new ArgumentOutOfRangeException(nameof(yEndExclusive));

        _passYStart = yStart;
        _passYEnd = yEndExclusive;

        _lastPlane = -1;
        _lastY = -1;
    }

    /// <summary>
    /// Releases the per-tile plane buffers for the tile (tileX, tileY).
    /// Call this after you have converted the tile planes to the final RGBA tile surface.
    /// </summary>
    public void ReleaseTilePlanes(int tileX, int tileY)
    {
        if ((uint)tileX >= (uint)_tilesX)
            throw new ArgumentOutOfRangeException(nameof(tileX));
        if ((uint)tileY >= (uint)_tilesY)
            throw new ArgumentOutOfRangeException(nameof(tileY));

        _tilePlanes[tileY * _tilesX + tileX] = null;
    }

    /// <summary>
    /// Releases the per-tile plane buffers by tile index (0..TileCount-1).
    /// </summary>
    public void ReleaseTilePlanes(int tileIndex)
    {
        if ((uint)tileIndex >= (uint)_tilePlanes.Length)
            throw new ArgumentOutOfRangeException(nameof(tileIndex));

        _tilePlanes[tileIndex] = null;
    }

    public void ConsumeRow(int planeIndex, int y, ReadOnlySpan<byte> row)
    {
        if ((uint)planeIndex >= (uint)_roles.Length)
            throw new ArgumentOutOfRangeException(nameof(planeIndex));
        if (y < _passYStart || y >= _passYEnd)
            throw new ArgumentOutOfRangeException(nameof(y));
        
        ValidateOrdering(planeIndex, y);

        var tileY = y / _tileHeight;
        var localY = y - tileY * _tileHeight;

        for (var tileX = 0; tileX < _tilesX; tileX++)
        {
            var index = tileY * _tilesX + tileX;
            var info = _tileInfos[index];

            if (localY >= info.Height)
                continue;

            var tile = _tilePlanes[index] ??= AllocateTilePlaneImage(info);

            var tilePlane = tile.Planes[planeIndex];
            var dst = tilePlane.Data.AsSpan(
                localY * tilePlane.BytesPerRow,
                info.Width * _bytesPerChannel);

            var srcXBytes = info.X * _bytesPerChannel;
            var srcLenBytes = info.Width * _bytesPerChannel;

            row.Slice(srcXBytes, srcLenBytes).CopyTo(dst);

            if (_onTileCompleted is not null
                && planeIndex == _roles.Length - 1
                && localY == info.Height - 1)
            {
                _onTileCompleted(tileX, tileY, tile);
            }
        }
    }

    private PlaneImage AllocateTilePlaneImage(TileInfo info)
    {
        var planes = new Plane[_roles.Length];

        var bytesPerRow = checked(info.Width * _bytesPerChannel);
        var planeSize = checked(bytesPerRow * info.Height);

        for (var i = 0; i < _roles.Length; i++)
            planes[i] = new Plane(_roles[i], new byte[planeSize], bytesPerRow);

        return new PlaneImage(info.Width, info.Height, checked(_bytesPerChannel * 8), planes);
    }

    private void ValidateOrdering(int planeIndex, int y)
    {
        if (_lastPlane < 0)
        {
            _lastPlane = planeIndex;
            _lastY = y;
            return;
        }

        if (planeIndex == _lastPlane)
        {
            if (y != _lastY + 1)
            {
                throw new InvalidOperationException(
                    $"TilePlaneImageRowSink requires plane-major sequential rows within a pass. " +
                    $"Expected y={_lastY + 1} for plane={planeIndex}, got y={y}.");
            }

            _lastY = y;
            return;
        }

        if (planeIndex != _lastPlane + 1 || y != _passYStart)
        {
            throw new InvalidOperationException(
                $"TilePlaneImageRowSink requires plane-major sequential rows within a pass. " +
                $"Expected plane={_lastPlane + 1}, y={_passYStart}; got plane={planeIndex}, y={y}.");
        }

        _lastPlane = planeIndex;
        _lastY = y;
    }
}