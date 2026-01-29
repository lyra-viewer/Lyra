using Lyra.Imaging.Psd.Core.Decode.Decompressors;

namespace Lyra.Imaging.Psd.Core.Decode.Pixel;

/// <summary>
/// Row sink that collects planar rows into a PlaneImage (full width, full height).
/// Intended as a bridge to reuse existing ColorModeProcessor logic.
/// </summary>
public sealed class PlaneImageRowSink : IPlaneRowConsumer
{
    private readonly PlaneImage _image;
    private readonly int _width;

    public PlaneImageRowSink(PlaneImage image)
    {
        _image = image;
        _width = image.Width;
    }

    public PlaneImage Image => _image;

    public void ConsumeRow(int planeIndex, int y, ReadOnlySpan<byte> row)
    {
        if ((uint)planeIndex >= (uint)_image.Planes.Count)
            throw new ArgumentOutOfRangeException(nameof(planeIndex));

        if ((uint)y >= (uint)_image.Height)
            throw new ArgumentOutOfRangeException(nameof(y));

        if (row.Length < _width)
            throw new ArgumentException($"Row span too small: {row.Length} < {_width}.", nameof(row));

        var plane = _image.Planes[planeIndex];

        row = row[.._width];
        row.CopyTo(plane.Data.AsSpan(y * plane.BytesPerRow, _width));
    }
}