using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
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
            Logger.Warning($"[SkiaDecoder] Failed to load {path}: {ex}");
            throw; // Propagate failure to Loader
        }

        return Task.CompletedTask;
    }
}