using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Pipeline;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Codecs;

internal class SkiaDecoder : IImageDecoder
{
    public bool CanDecode(ImageFormatType format) => format
        is ImageFormatType.Bmp
        or ImageFormatType.Ico
        or ImageFormatType.Jfif
        or ImageFormatType.Jpeg
        or ImageFormatType.Png
        or ImageFormatType.Webp;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[SkiaDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");
        
        composite.ExifInfo = MetadataProcessor.ParseMetadata(path);
        
        try
        {
            ct.ThrowIfCancellationRequested();

            using var stream = DecoderIO.OpenSequentialRead(path);

            using var codec = SKCodec.Create(stream);
            if (codec == null)
            {
                Logger.Warning($"[SkiaDecoder] Unable to create codec for: {path}");
                return Task.CompletedTask;
            }

            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var bitmap = new SKBitmap(info);

            // Ensure deterministic output if the image is truncated (IncompleteInput).
            bitmap.Erase(SKColors.Transparent);

            var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
            
            // Tries to repair if the image is truncated JPEG
            if (result == SKCodecResult.InvalidInput)
            {
                var repaired = TryDecodeJpegWithEoiRepair(path, bitmap);
                if (repaired.HasValue)
                {
                    result = repaired.Value;
                    Logger.Warning($"[SkiaDecoder] Recovered truncated JPEG via EOI repair: {path}");
                }
            }

            if (result == SKCodecResult.IncompleteInput)
                Logger.Warning($"[SkiaDecoder] Incomplete input (truncated image): {path}");

            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            {
                bitmap.Dispose();
                Logger.Warning($"[SkiaDecoder] Decode failed with status: {result}");
                return Task.CompletedTask;
            }

            ct.ThrowIfCancellationRequested();

            bitmap.SetImmutable();
            var image = SKImage.FromBitmap(bitmap);

            // Important: keep bitmap alive for image lifetime
            composite.Content = new RasterContent(bitmap, image);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancel to Loader
        }
        catch (Exception ex)
        {
            Logger.Warning($"[SkiaDecoder] Failed to load {path}: {ex.Message}");
            throw; // Propagate failure to Loader
        }

        return Task.CompletedTask;
    }
    
    private static SKCodecResult? TryDecodeJpegWithEoiRepair(string path, SKBitmap bitmap)
    {
        var bytes = File.ReadAllBytes(path);

        // JPEG SOI marker (FF D8).
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            return null;

        // Already has an EOI marker - nothing to repair.
        if (bytes[^2] == 0xFF && bytes[^1] == 0xD9)
            return null;

        var repaired = new byte[bytes.Length + 2];
        bytes.CopyTo(repaired, 0);
        repaired[^2] = 0xFF;
        repaired[^1] = 0xD9;

        using var stream = new MemoryStream(repaired, writable: false);
        using var codec = SKCodec.Create(stream);
        if (codec == null)
            return null;

        bitmap.Erase(SKColors.Transparent);
        return codec.GetPixels(bitmap.Info, bitmap.GetPixels());
    }
}