using Lyra.Common;
using Lyra.Imaging.Content.Tiling;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Tiff;

/// <summary>Decodes one tile of a streamed sheet straight out of the file.</summary>
internal sealed class TiffRegionTileProvider(string path, int directory, int width, int height, bool colour, bool premultiplied, uint bandRows)
    : ITileProvider
{
    private int Channels => colour ? 4 : 1;

    /// <summary>How many reads must time out in a row before this file is given up on.</summary>
    private const int TimeoutsBeforeGivingUp = 3;

    private int _consecutiveTimeouts;

    /// <summary>
    /// Set once reads have timed out often enough in a row to call the source wedged rather
    /// than slow. Every later tile would spend the whole bound finding that out again, so the
    /// image stops asking and keeps the preview it already has. Reopening it starts a new
    /// provider, and a new chance.
    /// </summary>
    private int _stalled;

    private void RecordTimeout()
    {
        if (Interlocked.Increment(ref _consecutiveTimeouts) < TimeoutsBeforeGivingUp)
            return;

        if (Interlocked.Exchange(ref _stalled, 1) == 0)
            Logger.Warning($"[TiffDecoder] {TimeoutsBeforeGivingUp} reads of {Path.GetFileName(path)} in a row did not " +
                           "return in time; no further tiles will be requested for it. The preview stays on screen.");
    }

    private void RecordSuccess() => Interlocked.Exchange(ref _consecutiveTimeouts, 0);

    public SKImage? Decode(int level, int tileX, int tileY, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (Volatile.Read(ref _stalled) != 0)
            return null;

        var span = DecodePolicy.TileEdge << level;

        var x = (long)tileX * span;
        var y = (long)tileY * span;

        if (x >= width || y >= height)
            return null;

        var regionWidth = (uint)Math.Min(span, width - x);
        var regionHeight = (uint)Math.Min(span, height - y);

        var bitmap = level == 0
            ? DecodeDirect((uint)x, (uint)y, regionWidth, regionHeight, ct)
            : DecodeReduced((uint)x, (uint)y, regionWidth, regionHeight, ct);

        return bitmap is null ? null : SKImage.FromBitmap(bitmap);
    }

    /// <summary>Full resolution: the region is the tile, so it is copied straight across.</summary>
    private SKBitmap? DecodeDirect(uint x, uint y, uint regionWidth, uint regionHeight, CancellationToken ct)
    {
        var read = TiffReads.Region(path, directory, colour, x, y, regionWidth, regionHeight, $"Tile {x},{y} of {Path.GetFileName(path)}", ct);
        if (!read.HasPixels)
        {
            if (read.TimedOut)
                RecordTimeout();
            else
                Warn(x, y, read.Error);

            return null;
        }

        RecordSuccess();

        try
        {
            var info = colour
                ? new SKImageInfo((int)regionWidth, (int)regionHeight, SKColorType.Rgba8888, premultiplied ? SKAlphaType.Premul : SKAlphaType.Unpremul)
                : new SKImageInfo((int)regionWidth, (int)regionHeight, SKColorType.Gray8, SKAlphaType.Opaque);

            var bitmap = new SKBitmap(info);
            PixelCopy.CopyRows(read.Pixels, read.Stride, bitmap);
            bitmap.SetImmutable();

            return bitmap;
        }
        finally
        {
            TiffNative.free_tiff_pixels(read.Pixels);
        }
    }

    /// <summary>
    /// A reduced level: the region is read a band at a time and averaged down, so a tile
    /// covering sixteen thousand pixels of sheet never exists at that size.
    /// </summary>
    private SKBitmap? DecodeReduced(uint x, uint y, uint regionWidth, uint regionHeight, CancellationToken ct)
    {
        bool ReadBand(uint first, uint rows, out IntPtr pixels, out uint stride)
        {
            var read = TiffReads.Region(path, directory, colour, x, y + first, regionWidth, rows, $"Reduced band {first}..{first + rows} of tile {x},{y} in {Path.GetFileName(path)}", ct);

            pixels = read.Pixels;
            stride = read.Stride;

            if (read.Ok)
                RecordSuccess();
            else if (read.TimedOut)
                RecordTimeout();
            else
                Warn(x, y + first, read.Error);

            return read.Ok;
        }

        return StreamingPreview.Build(
            (int)regionWidth, (int)regionHeight, DecodePolicy.TileEdge, DecodePolicy.TileEdge,
            ReadBand,
            TiffNative.free_tiff_pixels,
            bandRows,
            Channels,
            ct,
            premultiplied
        );
    }

    private void Warn(uint x, uint y, string nativeError) =>
        Logger.Warning($"[TiffDecoder] Region {x},{y} of {Path.GetFileName(path)} failed: {nativeError}");

    public void Dispose() { }
}