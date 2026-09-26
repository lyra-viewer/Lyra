using Lyra.Common;
using Lyra.Common.Estimation;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Tiling;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Tiff;

/// <summary>
/// Reads directories by region at one byte per pixel for gray and four for color; sheets too
/// large to hold are streamed as a preview plus tiles.
/// </summary>
internal static class TiffRegion
{
    internal static bool WantsNativeDepth(TiffNative.DirectoryInfo info)
    {
        if (info.RegionCapable == 0 || info.Width == 0 || info.Height == 0)
            return false;

        return (long)info.Width * info.Height * 4 > DecodePolicy.RgbaFormCeilingBytes;
    }

    internal static bool WantsStreaming(TiffNative.DirectoryInfo info) =>
        (long)info.Width * info.Height * info.RegionSamples > DecodePolicy.SingleTextureCeilingBytes;

    internal static bool IsColour(TiffNative.DirectoryInfo info) => info.RegionSamples == 4;

    private static SKImageInfo RegionImageInfo(TiffNative.DirectoryInfo info, int width, int height) =>
        IsColour(info)
            ? new SKImageInfo(width, height, SKColorType.Rgba8888, info.RegionPremultiplied != 0 ? SKAlphaType.Premul : SKAlphaType.Unpremul)
            : new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque);

    #region Whole directory

    /// <param name="fetched">The file already in memory; read from the path when null.</param>
    public static SKBitmap DecodeWhole(string path, int directory, TiffNative.DirectoryInfo info, Composite composite, CancellationToken ct,
        NativeFileBuffer? fetched = null)
    {
        ct.ThrowIfCancellationRequested();

        var width = (int)info.Width;
        var height = (int)info.Height;

        DecoderValidation.RequireSaneDimensions(width, height);

        var saving = $"[TiffDecoder] {Path.GetFileName(path)} is {width}x{height} at {info.BitsPerSample}-bit " +
                     $"{(IsColour(info) ? "colour" : "grey")}: " +
                     $"{(long)width * height * info.RegionSamples / (1024 * 1024)} MB read at its own depth, against " +
                     $"{(long)width * height * 4 / (1024 * 1024)} MB through the RGBA interface.";

        if (WantsNativeDepth(info))
            Logger.Info(saving);
        else
            Logger.Debug(saving);

        TiffRead read;

        if (fetched is not null && TiffNative.MemoryRegionAvailable)
        {
            read = TiffReads.RegionFromMemory(fetched, directory, IsColour(info), 0, 0, info.Width, info.Height);
        }
        else
        {
            var transfer = new IoTally(composite);
            read = TiffReads.Region(path, directory, IsColour(info), 0, 0, info.Width, info.Height, $"Full region of {Path.GetFileName(path)}", ct, transfer);

            transfer.ReportTo(composite);
        }

        if (!read.HasPixels)
            throw read.ToFailure($"Failed to decode {path} at native depth.");

        try
        {
            ct.ThrowIfCancellationRequested();

            var bitmap = new SKBitmap(RegionImageInfo(info, width, height));

            PixelCopy.CopyRows(read.Pixels, read.Stride, bitmap);

            return bitmap;
        }
        finally
        {
            TiffNative.free_tiff_pixels(read.Pixels);
        }
    }

    #endregion

    #region Streamed

    public static ICompositeContent Stream(string path, int directory, TiffNative.DirectoryInfo info, Composite composite, CancellationToken ct)
    {
        var width = (int)info.Width;
        var height = (int)info.Height;

        DecoderValidation.RequireSaneDimensions(width, height);

        Logger.Debug(typeof(TiffDecoder),
            $"{Path.GetFileName(path)} is {width}x{height} at {info.BitsPerSample}-bit " +
            $"{(IsColour(info) ? "colour" : "grey")} - " +
            $"{(long)width * height * info.RegionSamples / (1024 * 1024)} MB if it were held whole. Streaming a preview and " +
            "decoding tiles by region instead.");

        var content = new RasterLargeContent(width, height);

        composite.FullWidth = width;
        composite.FullHeight = height;

        try
        {
            var (maxWidth, maxHeight) = RasterContentBuilder.PreviewBounds();

            var colour = IsColour(info);
            var premultiplied = info.RegionPremultiplied != 0;
            var bandRows = BandRowsFor(path, info);

            var transfer = new IoTally(composite);

            var preview = StreamingPreview.Build(
                width, height, maxWidth, maxHeight,
                FullWidthBands(path, directory, info, "Preview", ct, transfer),
                TiffNative.free_tiff_pixels,
                bandRows,
                colour ? 4 : 1,
                ct,
                premultiplied
            );

            transfer.ReportTo(composite);

            if (preview is not null)
                content.SetPreview(SKImage.FromBitmap(preview));

            var tilesX = (width + DecodePolicy.TileEdge - 1) / DecodePolicy.TileEdge;
            var tilesY = (height + DecodePolicy.TileEdge - 1) / DecodePolicy.TileEdge;

            var maxLevel = LevelsDownTo(width, height, preview?.Width ?? 0, preview?.Height ?? 0);

            var tiles = new LazyTileSource(
                tilesX, tilesY, DecodePolicy.TileEdge, DecodePolicy.TileEdge,
                new TiffRegionTileProvider(path, directory, width, height, colour, premultiplied, bandRows),
                DecodePolicy.ResidentTileBudgetBytes,
                bytesPerPixel: colour ? 4 : 1,
                maxLevel: maxLevel
            );

            content.SetTiles(tiles);
            content.MarkAllTilesReady(tilesX * tilesY);

            return content;
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    public static SKBitmap? StreamThumbnail(string path, int directory, TiffNative.DirectoryInfo info, int maxDimension, CancellationToken ct)
    {
        var width = (int)info.Width;
        var height = (int)info.Height;

        DecoderValidation.RequireSaneDimensions(width, height);

        return StreamingPreview.Build(
            width, height, maxDimension, maxDimension,
            FullWidthBands(path, directory, info, "Thumbnail", ct),
            TiffNative.free_tiff_pixels,
            BandRowsFor(path, info),
            IsColour(info) ? 4 : 1,
            ct,
            info.RegionPremultiplied != 0
        );
    }

    private static StreamingPreview.BandReader FullWidthBands(string path, int directory, TiffNative.DirectoryInfo info, string purpose, CancellationToken ct, IoTally? tally = null) =>
        (uint first, uint rows, out IntPtr pixels, out uint stride) =>
        {
            var read = TiffReads.Region(path, directory, IsColour(info), 0, first, info.Width, rows,
                $"{purpose} band {first}..{first + rows} of {Path.GetFileName(path)}", ct, tally);

            pixels = read.Pixels;
            stride = read.Stride;

            return read.Ok;
        };

    /// <summary>Halvings needed to bring the sheet down to roughly the preview's size.</summary>
    private static int LevelsDownTo(int width, int height, int previewWidth, int previewHeight)
    {
        if (previewWidth <= 0 || previewHeight <= 0)
            return 0;

        var ratio = Math.Max(width / (double)previewWidth, height / (double)previewHeight);
        if (ratio <= 1)
            return 0;

        return Math.Clamp((int)Math.Floor(Math.Log2(ratio)), 0, 8);
    }

    #endregion

    #region Band height

    private const uint DefaultPreviewBandRows = 256;

    /// <summary>
    /// Rows per streaming read: bounded by memory, and by what <paramref name="source"/> delivers
    /// within <see cref="DecodePolicy.SlowSourceBandTargetMs"/>.
    /// </summary>
    internal static uint PreviewBandRowsFor(TiffNative.DirectoryInfo info, long encodedBytesPerRow = 0, TransferEstimate? source = null)
    {
        var bytesPerRow = Math.Max(1L, (long)info.Width * Math.Max((byte)1, info.RegionSamples));
        var budgetRows = (uint)Math.Clamp(DecodePolicy.PreviewBandBudgetBytes / bytesPerRow, 1, uint.MaxValue);

        var unit = info.IsTiled != 0 ? info.TileHeight : info.RowsPerStrip;

        var unaligned = unit == 0 || unit > info.Height;

        uint rows;
        if (unaligned)
            rows = Math.Min(DefaultPreviewBandRows, budgetRows);
        else if (unit >= budgetRows)
            rows = budgetRows;
        else
            rows = unit * (budgetRows / unit);

        if (source is not { BytesPerMs: > 0 } speed || encodedBytesPerRow <= 0)
            return rows;

        var sourceRows = (uint)Math.Clamp(DecodePolicy.SlowSourceBandTargetMs * speed.BytesPerMs / encodedBytesPerRow, 1, uint.MaxValue);

        // Never below one strip or tile, which is always fetched whole.
        if (!unaligned)
            sourceRows = Math.Max(unit, unit * (sourceRows / unit));

        return Math.Min(rows, sourceRows);
    }

    private static uint BandRowsFor(string path, TiffNative.DirectoryInfo info)
    {
        var fileBytes = TiffReads.SafeFileLength(path);
        var encodedBytesPerRow = info.Height > 0 ? fileBytes / info.Height : 0;
        var source = SourceThroughputEstimator.EstimateTransfer(path);

        var rows = PreviewBandRowsFor(info, encodedBytesPerRow, source);

        if (source is { } speed && rows < PreviewBandRowsFor(info))
            Logger.Debug(typeof(TiffDecoder), $"Reading {Path.GetFileName(path)} {rows} rows at a time to suit its source (~{speed.BytesPerMs * 1000 / (1024 * 1024):F1} MB/s).");

        return rows;
    }

    #endregion
}
