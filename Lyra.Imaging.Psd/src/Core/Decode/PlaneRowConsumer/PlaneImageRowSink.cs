using Lyra.Imaging.Psd.Core.Decode.Pixel;

namespace Lyra.Imaging.Psd.Core.Decode.PlaneRowConsumer;

/// <summary>
/// Row sink that collects planar rows into a PlaneImage (full width, full height).
/// Intended as a bridge to reuse existing ColorModeProcessor logic.
/// </summary>
public sealed class PlaneImageRowSink : IPlaneRowConsumer
{
    private readonly PlaneImage _image;
    private readonly int _rowBytes;

    public PlaneImageRowSink(PlaneImage image)
    {
        _image = image;

        var bpc = image.BitsPerChannel switch
        {
            8 => 1,
            16 => 2,
            32 => 4,
            _ => throw new NotSupportedException($"PlaneImageRowSink: BitsPerChannel {image.BitsPerChannel} not supported (expected 8/16/32).")
        };

        _rowBytes = checked(image.Width * bpc);
    }

    public PlaneImage Image => _image;

    public void ConsumeRow(int planeIndex, int y, ReadOnlySpan<byte> row)
    {
        if ((uint)planeIndex >= (uint)_image.Planes.Count)
            throw new ArgumentOutOfRangeException(nameof(planeIndex));

        if ((uint)y >= (uint)_image.Height)
            throw new ArgumentOutOfRangeException(nameof(y));

        if (row.Length < _rowBytes)
            throw new ArgumentException($"Row span too small: {row.Length} < {_rowBytes}.", nameof(row));

        var plane = _image.Planes[planeIndex];

        if (plane.BytesPerRow < _rowBytes)
            throw new InvalidOperationException($"Plane BytesPerRow too small: {plane.BytesPerRow} < {_rowBytes}.");

        row[.._rowBytes].CopyTo(plane.Data.AsSpan(y * plane.BytesPerRow, _rowBytes));
    }
}