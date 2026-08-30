using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Metadata;
using SkiaSharp;
using static System.Threading.Thread;
using Lyra.Imaging.Decoding.Support;

namespace Lyra.Imaging.Decoding.Decoders;

internal class SkiaDecoder : IImageDecoder, IThumbnailDecoder
{
    public bool CanDecode(ImageFormatType format) => format
        is ImageFormatType.Bmp
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

            var srcColorSpace = codec.Info.ColorSpace ?? SKColorSpace.CreateSrgb();
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul, srcColorSpace);
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
            
            var upright = OrientationTransform.Apply(bitmap, codec.EncodedOrigin);
            
            if (!ReferenceEquals(upright, bitmap))
                composite.AppliedOrientation = (ExifOrientation)codec.EncodedOrigin;

            bitmap = upright;

            // The builder takes ownership of the bitmap and keeps it alive for the image it makes.
            composite.Content = RasterContentBuilder.Build(bitmap, composite);
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

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var stream = DecoderIO.OpenSequentialRead(path);
        using var codec = SKCodec.Create(stream);
        if (codec is null)
            return null;

        var (fullWidth, fullHeight) = (codec.Info.Width, codec.Info.Height);
        if (fullWidth <= 0 || fullHeight <= 0)
            return null;

        // Request a downscale; the codec snaps to the nearest scale it can do natively
        // (JPEG: DCT 1, 1/2, 1/4, 1/8). For codecs without native scaling this returns
        // the full size, and the final downscale to the hash grid happens in the caller.
        var desiredScale = Math.Min(1f, (float)maxDimension / Math.Max(fullWidth, fullHeight));
        var scaled = codec.GetScaledDimensions(desiredScale);

        var info = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        bitmap.Erase(SKColors.Transparent); // deterministic output on truncated input

        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            return null;
        }

        return OrientationTransform.Apply(bitmap, codec.EncodedOrigin);
    }
}