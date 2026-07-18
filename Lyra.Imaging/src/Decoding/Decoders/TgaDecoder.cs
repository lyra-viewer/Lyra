using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Pipeline;
using Lyra.ManagedCodecs.Raster;
using SkiaSharp;
using static System.Threading.Thread;
using Lyra.ManagedCodecs.Raster.Tga;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class TgaDecoder : IImageDecoder, IThumbnailDecoder
{
    // The pipeline routes by file extension, so a file named *.tga may actually be another
    // format. TGA has no magic number; when the bytes do not structurally look like a TGA
    // (or managed decode fails on an unsupported variant), defer to the content-sniffing Skia
    // decoder, which auto-detects the real format (PNG/JPEG/BMP/WebP/...) regardless of extension.
    private static readonly SkiaDecoder Fallback = new();

    public bool CanDecode(ImageFormatType format) => format is ImageFormatType.Tga;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[TgaDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        composite.ExifInfo = MetadataProcessor.ParseMetadata(path);

        ct.ThrowIfCancellationRequested();

        var bytes = File.ReadAllBytes(path);
        if (!TryDecodeManaged(bytes, path, out var decoded))
        {
            return Fallback.DecodeAsync(composite, ct);
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            DecoderValidation.RequireSaneDimensions(GetType().Name, decoded.Width, decoded.Height);

            var bitmap = ToSkBitmap(decoded);
            bitmap.SetImmutable();
            var skImage = SKImage.FromBitmap(bitmap);
            composite.Content = new RasterContent(bitmap, skImage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.Warning($"[TgaDecoder] Image could not be loaded: {path}\n{e.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var bytes = File.ReadAllBytes(path);
        if (!TryDecodeManaged(bytes, path, out var decoded))
        {
            return Fallback.DecodeThumbnail(path, maxDimension, ct);
        }

        ct.ThrowIfCancellationRequested();

        // TGA has no embedded/native scaled decode, so decode full then downscale.
        return ThumbnailScaler.ResizeToThumbnail(ToSkBitmap(decoded), maxDimension);
    }

    /// <summary>
    /// Attempts a managed TGA decode. Returns false (signaling the caller to fall back to the
    /// content-sniffing decoder) when the bytes are not a TGA or the variant is unsupported.
    /// </summary>
    private static bool TryDecodeManaged(byte[] bytes, string path, out DecodedImage decoded)
    {
        if (!TgaReader.CanDecode(bytes))
        {
            Logger.Warning($"[TgaDecoder] {path} does not look like a TGA (extension mismatch); falling back to {nameof(SkiaDecoder)}.");
            decoded = default;
            return false;
        }

        try
        {
            decoded = TgaReader.Decode(bytes);
            return true;
        }
        catch (Exception e)
        {
            Logger.Warning($"[TgaDecoder] Managed TGA decode failed for {path} ({e.Message}); falling back to {nameof(SkiaDecoder)}.");
            decoded = default;
            return false;
        }
    }

    private static SKBitmap ToSkBitmap(DecodedImage decoded)
    {
        var info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);

        PixelCopy.CopyTightRgba(decoded.Pixels.AsSpan(0, decoded.Width * decoded.Height * 4), bitmap);

        return bitmap;
    }
}