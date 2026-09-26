using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Metadata;
using Lyra.ManagedCodecs.Raster;
using SkiaSharp;
using Lyra.ManagedCodecs.Raster.Tga;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class TgaDecoder : DecoderBase, IThumbnailDecoder
{
    // The pipeline routes by file extension, so a file named *.tga may actually be another
    // format. TGA has no magic number; when the bytes do not structurally look like a TGA
    // (or managed decode fails on an unsupported variant), defer to the content-sniffing Skia
    // decoder, which auto-detects the real format (PNG/JPEG/BMP/WebP/...) regardless of extension.
    private static readonly SkiaDecoder Fallback = new();

    public override bool CanDecode(ImageFormatType format) => format is ImageFormatType.Tga;

    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        var bytes = composite.ReadAllBytes(ct);
        composite.ExifInfo = ReadMetadata(bytes, path);

        // Some files under .tga are not TGA at all. Skia gets a turn before the file is refused,
        // and it renames the composite to itself on the way through.
        if (!TryDecodeManaged(bytes, path, out var decoded))
        {
            Fallback.DecodeAsync(composite, ct).GetAwaiter().GetResult();
            return;
        }

        ct.ThrowIfCancellationRequested();
        DecoderValidation.RequireSaneDimensions(decoded.Width, decoded.Height);

        var bitmap = ToSkBitmap(decoded);
        composite.Content = RasterContentBuilder.Build(bitmap, composite);
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var bytes = DecoderIO.ReadAllBytes(path, ct, out _);
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

    private static ExifInfo ReadMetadata(byte[] data, string path)
    {
        using var stream = new MemoryStream(data, writable: false);
        return MetadataProcessor.ParseMetadata(stream, path);
    }
}