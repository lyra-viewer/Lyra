using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Exif;
using Lyra.Imaging.Content.Tiling;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Metadata;
using Lyra.Metadata.Photoshop;
using Lyra.Psd;
using Lyra.Psd.Core.Decode.Composite;
using Lyra.Psd.Core.SectionData;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

internal class PsdDecoder : DecoderBase
{
    public override IReadOnlyList<ImageFormatType> Formats { get; } = [ImageFormatType.Psd, ImageFormatType.Psb];

    protected override void Decode(DecodeContext context, CancellationToken ct)
    {
        var path = context.Source.RequireFile();

        var header = ReadAndProcessHeader(path, context);
        var isLarge = !DecodePolicy.FitsOneTexture(header.Width, header.Height, bytesPerPixel: 4);

        if (!isLarge)
            DecodeSmallPsd(context, ct);
        else
            DecodeLargePsd(path, header, context, ct);
    }

    private static FileHeader ReadAndProcessHeader(string path, DecodeContext context)
    {
        try
        {
            using var stream = context.Source.OpenHead();
            var header = PsdDocument.ReadHeader(stream);

            context.SetFullSize(header.Width, header.Height);
            context.ReportPixelCount(header.Width, header.Height);
            context.AddFormatSpecific("Color Mode", $"{header.ColorMode}");
            context.AddFormatSpecific("Channels", $"{header.NumberOfChannels}");
            context.AddFormatSpecific("Depth per Channel", $"{header.Depth}-bit");

            return header;
        }
        catch (Exception e)
        {
            Logger.Warning($"[PsdDecoder] Header could not be read: {path}\n{e.Message}");
            throw;
        }
    }

    private static void DecodeSmallPsd(DecodeContext context, CancellationToken ct)
    {
        var path = context.Source.RequireFile();
        
        using var stream = new MeasuredReadStream(DecoderIO.OpenRandomAccessRead(path), context.ReportTransferred, context.CompleteTransfer);
        var psd = PsdDocument.ReadDocument(stream);
        context.ExifInfo = ReadMetadata(psd, stream, path);
        context.PsdLayers = psd.DecodeLayerRecords(stream);
        context.Structure = PsdStructure.Describe(psd);

        stream.Position = 0;
        using var surface = psd.Decode(stream, null, null, ct);

        ProcessMetadata(psd.PsdMetadata, context);
        context.Content = new RasterContent(ToImage(surface));
    }

    #region Large PSD (preview + tiled decode)

    private void DecodeLargePsd(string path, FileHeader header, DecodeContext context, CancellationToken ct)
    {
        var rasterLarge = new RasterLargeContent(header.Width, header.Height);

        PsdDocument psd;
        TiledCompositeImage tiled;
        RasterTileSource tileSource;

        try
        {
            ct.ThrowIfCancellationRequested();

            using var stream = new MeasuredReadStream(DecoderIO.OpenRandomAccessRead(path), context.ReportTransferred, context.CompleteTransfer);
            psd = PsdDocument.ReadDocument(stream);
            context.ExifInfo = ReadMetadata(psd, stream, path);
            context.PsdLayers = psd.DecodeLayerRecords(stream);
            context.Structure = PsdStructure.Describe(psd);

            stream.Position = 0;
            DecodePreview(psd, stream, rasterLarge, context.Options, ct);

            tiled = psd.CreateTiledComposite(stream, maxBytesPerTile: DecodePolicy.MaxTileDecodeBytes, tileEdgeHint: null, outputFormat: null);
            tileSource = SetupTileSource(rasterLarge, tiled);
        }
        catch
        {
            rasterLarge.Dispose();
            throw;
        }

        // Published whole: the preview, and the grid the tiles stream into.
        context.Content = rasterLarge;
        context.SignalReady();

        ProcessMetadata(psd.PsdMetadata, context);

        ct.ThrowIfCancellationRequested();

        ScheduleTileDecode(path, psd, tiled, tileSource, rasterLarge, context, ct);
    }

    private static void DecodePreview(PsdDocument psd, Stream stream, RasterLargeContent rasterLarge, DecodeOptions options, CancellationToken ct)
    {
        var (maxWidth, maxHeight) = (options.PreviewWidth, options.PreviewHeight);

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
        DecodeContext context,
        CancellationToken ct)
    {
        // The tile pass holds its thread throughout, so it stays off the load's workers.
        context.ContinueInBackground(token =>
        {
            token.ThrowIfCancellationRequested();

            using var tileStream = new MeasuredReadStream(DecoderIO.OpenSequentialRead(path), context.ReportTransferred, context.CompleteTransfer);

            var bandOrder = TileDecodeScheduler.BuildBandOrder(tiled.TilesX, tiled.TilesY);

            Logger.Debug($"[PsdDecoder] Tiled: {tiled.TilesX}x{tiled.TilesY}, tile={tiled.TileWidth}x{tiled.TileHeight}");
            Logger.Debug($"[PsdDecoder] Compression: {psd.ImageData.CompressionType}");
            Logger.Debug($"[PsdDecoder] BandOrder head: {string.Join(", ", bandOrder.Take(8))}...");

            psd.DecodeTiles(
                tileStream,
                tiled,
                bandOrder,
                outputFormat: null,
                maxSurfaceBytes: null,
                onTileReady: (x, y) => OnTileReady(x, y, tiled, tileSource, rasterLarge, token),
                token);

            return Task.CompletedTask;
        }, BackgroundSchedule.ThreadPool, ct);
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

    /// <summary>Above this a resource is not believed to be metadata.</summary>
    private const int MaxResourceBytes = 64 * 1024 * 1024;

    /// <summary>The EXIF, XMP, IPTC and ICC among the image resources Lyra.Psd located, read from where it found them.</summary>
    private static ExifInfo ReadMetadata(PsdDocument psd, Stream stream, string path)
    {
        var resources = new List<PhotoshopResource>();
        var position = stream.Position;
        try
        {
            foreach (var block in psd.ImageResources.Blocks)
            {
                if (PhotoshopResources.BlockKind(block.Id) is null || block.DataSize > MaxResourceBytes || block.DataOffset + block.DataSize > stream.Length)
                    continue;

                var data = new byte[block.DataSize];
                stream.Position = block.DataOffset;
                stream.ReadExactly(data);
                resources.Add(new PhotoshopResource(block.Id, block.Name, data));
            }
        }
        catch (IOException e)
        {
            Logger.Warning($"[PsdDecoder] Image resources could not be read: {path}\n{e.Message}");
        }
        finally
        {
            stream.Position = position;
        }

        return MetadataProcessor.ParsePhotoshopResources(resources, path);
    }

    private static void ProcessMetadata(PsdMetadata metadata, DecodeContext context)
    {
        context.AddFormatSpecific("Compression", $"{metadata.CompressionType ?? "none"}");
        context.AddFormatSpecific("Embedded ICC Profile", $"{metadata.EmbeddedIccProfileName ?? "none"}");

        if (metadata.EffectiveIccProfileName is not "Embedded ICC Profile")
            context.AddFormatSpecific("Effective ICC Profile", $"{metadata.EffectiveIccProfileName ?? "none"}");
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