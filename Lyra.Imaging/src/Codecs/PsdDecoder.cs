using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.ConstraintsProvider;
using Lyra.Imaging.Content;
using Lyra.Imaging.Psd;
using Lyra.Imaging.Psd.Core.Decode.Composite;
using Lyra.Imaging.Psd.Core.Decode.Layers;
using Lyra.Imaging.Psd.Core.SectionData;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Codecs;

internal class PsdDecoder : IImageDecoder
{
    private const float PreviewSizeMultiplier = 2.0f;
    private const long LargePsdThresholdBytes = 256L * 1024 * 1024;
    private const long TileMaxBytes = 64L * 1024 * 1024;

    private readonly TileDecodeScheduler _tileDecodeScheduler = new();

    public bool CanDecode(ImageFormatType format) => format is ImageFormatType.Psd or ImageFormatType.Psb;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[PsdDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        ct.ThrowIfCancellationRequested();

        try
        {
            var header = ReadAndProcessHeader(path, composite);
            var isLarge = (long)header.Width * header.Height * 4L >= LargePsdThresholdBytes;

            if (!isLarge)
                DecodeSmallPsd(path, composite, ct);
            else
                DecodeLargePsd(path, header, composite, ct);

            return Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.Warning($"[PsdDecoder] Image could not be loaded: {path}\n{e}");
            throw;
        }
    }

    private static FileHeader ReadAndProcessHeader(string path, Composite composite)
    {
        try
        {
            using var stream = DecoderIO.OpenRandomAccessRead(path);
            var header = PsdDocument.ReadHeader(stream);

            composite.FullWidth = header.Width;
            composite.FullHeight = header.Height;
            composite.FormatSpecific.Add("Color Mode", $"{header.ColorMode}");
            composite.FormatSpecific.Add("Channels", $"{header.NumberOfChannels}");
            composite.FormatSpecific.Add("Depth per Channel", $"{header.Depth}-bit");

            return header;
        }
        catch (Exception e)
        {
            Logger.Warning($"[PsdDecoder] Header could not be read: {path}\n{e}");
            throw;
        }
    }

    private static void DecodeSmallPsd(string path, Composite composite, CancellationToken ct)
    {
        using var stream = DecoderIO.OpenRandomAccessRead(path);
        var psd = PsdDocument.ReadDocument(stream);

        ProcessLayerNames(psd.DecodeLayerRecords(stream));

        stream.Position = 0;
        using var surface = psd.Decode(stream, null, null, ct);

        ProcessMetadata(psd.PsdMetadata, composite);
        composite.Content = new RasterContent(ToImage(surface));
    }
    
    #region Large PSD (preview + tiled decode)

    private void DecodeLargePsd(string path, FileHeader header, Composite composite, CancellationToken ct)
    {
        var rasterLarge = new RasterLargeContent(header.Width, header.Height);
        composite.Content = rasterLarge;

        ct.ThrowIfCancellationRequested();

        using var stream = DecoderIO.OpenRandomAccessRead(path);
        var psd = PsdDocument.ReadDocument(stream);
        
        ProcessLayerNames(psd.DecodeLayerRecords(stream));

        stream.Position = 0;
        DecodePreview(psd, stream, rasterLarge, ct);
        composite.SignalReady();

        ProcessMetadata(psd.PsdMetadata, composite);

        ct.ThrowIfCancellationRequested();
        stream.Position = 0;

        var tiled = psd.CreateTiledComposite(stream, maxBytesPerTile: TileMaxBytes, tileEdgeHint: null, outputFormat: null);
        var tileSource = SetupTileSource(rasterLarge, tiled);

        ScheduleTileDecode(path, psd, tiled, tileSource, rasterLarge, composite, ct);
    }

    private static void DecodePreview(PsdDocument psd, Stream stream, RasterLargeContent rasterLarge, CancellationToken ct)
    {
        var constraints = DecodeConstraintsProvider.Current;
        var maxWidth = (int)(constraints.LogicalWidth * PreviewSizeMultiplier);
        var maxHeight = (int)(constraints.LogicalHeight * PreviewSizeMultiplier);

        Logger.Debug($"[PsdDecoder] Preview size: {maxWidth}x{maxHeight}");

        stream.Position = 0;
        using var previewSurface = psd.DecodePreview(stream, maxWidth, maxHeight, outputFormat: null, maxSurfaceBytes: null, ct: ct);
        rasterLarge.SetPreview(ToImage(previewSurface));
    }

    private static RasterTileSource SetupTileSource(RasterLargeContent rasterLarge, TiledCompositeImage tiled)
    {
        var tileSource = new RasterTileSource(
            tilesX: tiled.TilesX,
            tilesY: tiled.TilesY,
            tileWidth: tiled.TileWidth,
            tileHeight: tiled.TileHeight);

        rasterLarge.SetTiles(tileSource);
        rasterLarge.SetTilesTotal(tiled.TilesX * tiled.TilesY);

        return tileSource;
    }

    private void ScheduleTileDecode(
        string path,
        PsdDocument psd,
        TiledCompositeImage tiled,
        RasterTileSource tileSource,
        RasterLargeContent rasterLarge,
        Composite composite,
        CancellationToken ct)
    {
        _ = Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                using var tileStream = DecoderIO.OpenSequentialRead(path);
                tileStream.Position = 0;

                var bandOrder = _tileDecodeScheduler.BuildBandOrder(tiled.TilesX, tiled.TilesY, tiled.TileWidth, tiled.TileHeight);

                Logger.Debug($"[PsdDecoder] Tiled: {tiled.TilesX}x{tiled.TilesY}, tile={tiled.TileWidth}x{tiled.TileHeight}");
                Logger.Debug($"[PsdDecoder] Compression: {psd.ImageData.CompressionType}");
                Logger.Debug($"[PsdDecoder] BandOrder head: {string.Join(", ", bandOrder.Take(8))}...");

                psd.DecodeTiles(
                    tileStream,
                    tiled,
                    bandOrder,
                    outputFormat: null,
                    maxSurfaceBytes: null,
                    onTileReady: (x, y) => OnTileReady(x, y, tiled, tileSource, rasterLarge, ct),
                    ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Warning($"[PsdDecoder] Tile decode failed: {path}\n{ex}");
            }
            finally
            {
                composite.SignalComplete();
            }
        }, ct);
    }

    private static void OnTileReady(
        int x, int y,
        TiledCompositeImage tiled,
        RasterTileSource tileSource,
        RasterLargeContent rasterLarge,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var tileSurface = tiled.TryGetTile(x, y);
        if (tileSurface is null)
            return;

        var tileImage = ToImage(tileSurface);
        tileSurface.Dispose();

        tileSource.SetTile(x, y, tileImage);
        rasterLarge.IncrementTileReady();
    }

    #endregion

    #region Metadata

    private static void ProcessMetadata(PsdMetadata metadata, Composite composite)
    {
        composite.FormatSpecific.Add("Compression", $"{metadata.CompressionType ?? "none"}");
        composite.FormatSpecific.Add("Embedded ICC Profile", $"{metadata.EmbeddedIccProfileName ?? "none"}");

        if (metadata.EffectiveIccProfileName is not "Embedded ICC Profile")
            composite.FormatSpecific.Add("Effective ICC Profile", $"{metadata.EffectiveIccProfileName ?? "none"}");
    }

    private static void ProcessLayerNames(LayerRecord[] layers)
    {
        // TODO not used, not planned yet
    }

    #endregion

    #region Skia Conversion

    private static readonly SKImageRasterReleaseDelegate ReleaseBitmapOnImageDispose = (_, ctx) =>
    {
        if (ctx is SKBitmap bmp && bmp.Handle != IntPtr.Zero)
            bmp.Dispose();
    };

    private static SKImage ToImage(RgbaSurface surface)
    {
        var bmp = ToBitmap(surface);
        bmp.SetImmutable();

        using var pixmap = new SKPixmap(bmp.Info, bmp.GetPixels(), bmp.RowBytes);
        return SKImage.FromPixels(pixmap, ReleaseBitmapOnImageDispose, bmp);
    }

    private static SKBitmap ToBitmap(RgbaSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var (colorType, alphaType) = MapFormat(surface.Format);
        var info = new SKImageInfo(surface.Width, surface.Height, colorType, alphaType);

        var bitmap = new SKBitmap(info);
        var dstRowBytes = bitmap.RowBytes;

        unsafe
        {
            var dstBase = (byte*)bitmap.GetPixels().ToPointer();

            for (var y = 0; y < surface.Height; y++)
            {
                var srcRow = surface.GetRowSpan(y);
                var dstRow = new Span<byte>(dstBase + y * dstRowBytes, dstRowBytes);
                srcRow.CopyTo(dstRow);
            }
        }

        return bitmap;
    }

    private static (SKColorType colorType, SKAlphaType alphaType) MapFormat(SurfaceFormat format) =>
        format.PixelFormat switch
        {
            PixelFormat.Bgra8888 => (SKColorType.Bgra8888, MapAlpha(format.AlphaType)),
            PixelFormat.Rgba8888 => (SKColorType.Rgba8888, MapAlpha(format.AlphaType)),
            _ => throw new NotSupportedException($"Unsupported pixel format for Skia conversion: {format.PixelFormat}.")
        };

    private static SKAlphaType MapAlpha(AlphaType alphaType) =>
        alphaType switch
        {
            AlphaType.Premultiplied => SKAlphaType.Premul,
            AlphaType.Straight => SKAlphaType.Unpremul,
            _ => throw new ArgumentOutOfRangeException(nameof(alphaType))
        };

    #endregion
}