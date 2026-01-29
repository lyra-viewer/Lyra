using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.ConstraintsProvider;
using Lyra.Imaging.Content;
using Lyra.Imaging.Psd;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.SectionData;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Codecs;

internal class PsdDecoder : IImageDecoder
{
    private const float PreviewSizeMultiplier = 2.0f;
    
    private readonly TileDecodeScheduler _tileDecodeScheduler = new();
    
    public bool CanDecode(ImageFormatType format) => format is ImageFormatType.Psd or ImageFormatType.Psb;

    public async Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[PsdDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        ct.ThrowIfCancellationRequested();
        
        var fileStreamOptions = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 4096,
            Options = FileOptions.RandomAccess
        };

        await using var file = new FileStream(path, fileStreamOptions);

        FileHeader header;
        try
        {
            header = PsdDocument.ReadHeader(file);
            ProcessHeader(header, composite);
        }
        catch (Exception e)
        {
            Logger.Warning($"[PsdDecoder] Header could not be read: {path}\n{e.Message}");
            return;
        }

        // Heuristic: treat as "large" if RGBA8 would be big.
        // (Starting point 256 MB.)
        var rgbaBytes = (long)header.Width * header.Height * 4L;
        var isLarge = rgbaBytes >= 256L * 1024 * 1024;

        try
        {
            if (!isLarge)
            {
                // Small PSD: decode full surface -> SKImage.
                var (skImage, metadata) = await Task.Run(() =>
                {
                    using var file1 = File.Open(path, fileStreamOptions);
                    var psd = PsdDocument.ReadDocument(file1);
                    using var surface = psd.Decode(file1, null, null, ct);
                    return (ToImage(surface), psd.PsdMetadata);
                }, ct);

                ProcessMetadata(metadata, composite);
                composite.Content = new RasterContent(skImage);
                return;
            }

            var rasterLarge = new RasterLargeContent(header.Width, header.Height);
            composite.Content = rasterLarge;

            // Read PSD document
            var psdDocument = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                
                file.Position = 0;
                return PsdDocument.ReadDocument(file);
            }, ct);
            
            var constraints = DecodeConstraintsProvider.Current;
            var previewImage = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                file.Position = 0;
                using var previewSurface = psdDocument.DecodePreview(
                    file,
                    maxWidth: (int)(constraints.Width * PreviewSizeMultiplier),
                    maxHeight: (int)(constraints.Height * PreviewSizeMultiplier),
                    outputFormat: null,
                    maxSurfaceBytes: null,
                    ct: ct);

                return ToImage(previewSurface);
            }, ct);

            rasterLarge.SetPreview(previewImage);
            composite.SignalReady();
            
            ProcessMetadata(psdDocument.PsdMetadata, composite);

            // Create tiled container (geometry only)
            file.Position = 0;
            var tiled = psdDocument.CreateTiledComposite(
                file,
                maxBytesPerTile: 256L * 1024 * 1024, // start safe; tune later
                tileEdgeHint: null,
                outputFormat: null,
                ct: ct);

            // Tile source stores SKImages.
            var tileSource = new RasterTileSource(
                tilesX: tiled.TilesX,
                tilesY: tiled.TilesY,
                tileWidth: tiled.TileWidth,
                tileHeight: tiled.TileHeight);

            rasterLarge.SetTiles(tileSource);
            rasterLarge.SetTilesTotal(tiled.TilesX * tiled.TilesY);

            // Decode tiles on a background thread (keep DecodeAsync non-blocking for large images).
            _ = Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                using var tileFile = File.OpenRead(path);
                tileFile.Position = 0;

                var bandOrder = _tileDecodeScheduler.BuildBandOrder(
                    tiled.TilesX,
                    tiled.TilesY,
                    tiled.TileWidth,
                    tiled.TileHeight);

                Logger.Debug($"[PsdDecoder] Tiled: {tiled.TilesX}x{tiled.TilesY}, tile={tiled.TileWidth}x{tiled.TileHeight}");
                Logger.Debug($"[PsdDecoder] Compression: {psdDocument.ImageData.CompressionType}");
                Logger.Debug($"[PsdDecoder] BandOrder head: {string.Join(", ", bandOrder.Take(8))}...");

                psdDocument.DecodeTiles(
                    tileFile,
                    tiled,
                    bandOrder,
                    outputFormat: null,
                    maxSurfaceBytes: null,
                    onTileReady: (x, y) =>
                    {
                        ct.ThrowIfCancellationRequested();

                        var tileSurface = tiled.TryGetTile(x, y);
                        if (tileSurface is null)
                            return;

                        var tileImage = ToImage(tileSurface);
                        tileSurface.Dispose();

                        tileSource.SetTile(x, y, tileImage);
                        rasterLarge.IncrementTileReady();
                    }, ct);

                composite.SignalComplete();
            }, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Logger.Warning($"[PsdDecoder] Image could not be loaded: {path} \n{e.Message}");
        }
    }

    private void ProcessHeader(FileHeader header, Composite composite)
    {
        composite.FullWidth = header.Width;
        composite.FullHeight = header.Height;
        composite.FormatSpecific.Add("Color Mode", $"{header.ColorMode}");
        composite.FormatSpecific.Add("Channels", $"{header.NumberOfChannels}");
        composite.FormatSpecific.Add("Depth per Channel", $"{header.Depth}-bit");
    }

    private void ProcessMetadata(PsdMetadata metadata, Composite composite)
    {
        composite.FormatSpecific.Add("Compression", $"{metadata.CompressionType ?? "none"}");
        composite.FormatSpecific.Add("Embedded ICC Profile", $"{metadata.EmbeddedIccProfileName ?? "none"}");

        if (metadata.EffectiveIccProfileName is not "Embedded ICC Profile")
            composite.FormatSpecific.Add("Effective ICC Profile", $"{metadata.EffectiveIccProfileName ?? "none"}");
    }

    private static SKImage ToImage(RgbaSurface surface)
    {
        using var bmp = ToBitmap(surface);
        return SKImage.FromBitmap(bmp);
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

    private static (SKColorType colorType, SKAlphaType alphaType) MapFormat(SurfaceFormat format)
    {
        return format.PixelFormat switch
        {
            PixelFormat.Bgra8888 => (SKColorType.Bgra8888, MapAlpha(format.AlphaType)),
            PixelFormat.Rgba8888 => (SKColorType.Rgba8888, MapAlpha(format.AlphaType)),
            _ => throw new NotSupportedException($"Unsupported pixel format for Skia conversion: {format.PixelFormat}.")
        };
    }

    private static SKAlphaType MapAlpha(AlphaType alphaType) => alphaType switch
    {
        AlphaType.Premultiplied => SKAlphaType.Premul,
        AlphaType.Straight => SKAlphaType.Unpremul,
        _ => throw new ArgumentOutOfRangeException(nameof(alphaType))
    };
}