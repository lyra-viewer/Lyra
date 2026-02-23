using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Composite;
using Lyra.Imaging.Psd.Core.Decode.Pixel;

namespace Lyra.Imaging.Psd.Core.Decode.PlaneRowConsumer;

public sealed class TilePlaneImageRowSink : IPlaneRowConsumer
{
    private readonly int _tileWidth;
    private readonly int _tileHeight;

    private readonly int _tilesX;
    private readonly int _tilesY;

    private readonly PsdDepth _depth;
    private readonly int _bytesPerChannel;
    private readonly PlaneRole[] _roles;

    private readonly TileInfo[] _tileInfos;
    private readonly PlaneImage?[] _tilePlanes;

    // Fast-path cache: [ty][tx * planes + p] -> byte[]
    private readonly byte[][][] _tileBuffers;

    // Tracks completion state of each tile row
    private readonly int[] _rowsWrittenPerTileRow;

    private readonly Action<int, int, PlaneImage>? _onTileCompleted;

    public TilePlaneImageRowSink(int imageWidth, int imageHeight, int tileWidth, int tileHeight, int bitsPerChannel, PlaneRole[] roles, Action<int, int, PlaneImage>? onTileCompleted = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileHeight);
        
        _tileWidth = tileWidth;
        _tileHeight = tileHeight;

        _depth = PsdDepthUtil.FromBitsPerChannel(bitsPerChannel);
        _bytesPerChannel = _depth.BytesPerChannel();
        _roles = roles;

        _tilesX = (imageWidth + _tileWidth - 1) / _tileWidth;
        _tilesY = (imageHeight + _tileHeight - 1) / _tileHeight;

        _tileInfos = new TileInfo[_tilesX * _tilesY];
        _tilePlanes = new PlaneImage?[_tilesX * _tilesY];

        _tileBuffers = new byte[_tilesY][][];
        _rowsWrittenPerTileRow = new int[_tilesY];

        _onTileCompleted = onTileCompleted;

        for (var ty = 0; ty < _tilesY; ty++)
        {
            for (var tx = 0; tx < _tilesX; tx++)
            {
                var startX = tx * _tileWidth;
                var startY = ty * _tileHeight;

                var width = Math.Min(_tileWidth, imageWidth - startX);
                var height = Math.Min(_tileHeight, imageHeight - startY);

                var index = ty * _tilesX + tx;
                _tileInfos[index] = new TileInfo(startX, startY, width, height, index);
            }
        }
    }

    public void ConsumeRow(int planeIndex, int y, ReadOnlySpan<byte> row)
    {
        var ty = Math.DivRem(y, _tileHeight, out var destY);
        if (_tileBuffers[ty] == null)
        {
            _tileBuffers[ty] = new byte[_tilesX * _roles.Length][];

            for (var tx = 0; tx < _tilesX; tx++)
            {
                var index = ty * _tilesX + tx;
                var info = _tileInfos[index];

                var tileImg = AllocateTilePlaneImage(info);
                _tilePlanes[index] = tileImg;

                for (var p = 0; p < _roles.Length; p++)
                {
                    _tileBuffers[ty][tx * _roles.Length + p] = tileImg.Planes[p].Data;
                }
            }
        }

        var buffers = _tileBuffers[ty];
        for (var tx = 0; tx < _tilesX; tx++)
        {
            var info = _tileInfos[ty * _tilesX + tx];

            var srcSlice = row.Slice(info.X * _bytesPerChannel, info.Width * _bytesPerChannel);

            var destArray = buffers[tx * _roles.Length + planeIndex];
            var destOffset = destY * info.Width * _bytesPerChannel;

            srcSlice.CopyTo(destArray.AsSpan(destOffset, srcSlice.Length));
        }

        _rowsWrittenPerTileRow[ty]++;

        var expectedRows = _tileInfos[ty * _tilesX].Height * _roles.Length;
        if (_rowsWrittenPerTileRow[ty] == expectedRows)
        {
            if (_onTileCompleted != null)
            {
                for (var tx = 0; tx < _tilesX; tx++)
                {
                    var tileIndex = ty * _tilesX + tx;
                    if (_tilePlanes[tileIndex].HasValue)
                    {
                        var tile = _tilePlanes[tileIndex]!.Value;
                        _tilePlanes[tileIndex] = null; // Free reference

                        _onTileCompleted(tx, ty, tile);
                    }
                }
            }

            // Nuke the cache for this row to allow aggressive Garbage Collection
            _tileBuffers[ty] = null!;
        }
    }

    private PlaneImage AllocateTilePlaneImage(TileInfo info)
    {
        var planes = new Plane[_roles.Length];
        var bytesPerRow = _depth.RowBytes(info.Width);
        var planeSize = checked(bytesPerRow * info.Height);

        for (var i = 0; i < _roles.Length; i++)
        {
            // Zero-cost allocation
            var buffer = GC.AllocateUninitializedArray<byte>(planeSize);
            planes[i] = new Plane(_roles[i], buffer, bytesPerRow);
        }

        return new PlaneImage(info.Width, info.Height, (int)_depth, planes);
    }
}