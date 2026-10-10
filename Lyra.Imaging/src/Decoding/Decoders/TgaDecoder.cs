using System.Text;
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
        // and it renames the composite to itself on the way through. A TGA Skia cannot read either
        // fails with its own reason rather than Skia's.
        if (!TryDecodeManaged(bytes, path, out var decoded, out var truncated, out var failure))
        {
            try
            {
                Fallback.DecodeAsync(composite, ct).GetAwaiter().GetResult();
            }
            catch (Exception e) when (failure is not null && e is not OperationCanceledException)
            {
                throw new LoadFailureException(LoadFailureKind.DecodeFailed, Reason(failure));
            }

            return;
        }

        ct.ThrowIfCancellationRequested();
        DecoderValidation.RequireSaneDimensions(decoded.Width, decoded.Height);

        Describe(composite, bytes);

        if (truncated)
            composite.Warning = LoadWarning.PartiallyDecoded("The file ends before the image does");

        var bitmap = ToSkBitmap(decoded);
        composite.Content = RasterContentBuilder.Build(bitmap, composite);
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var bytes = DecoderIO.ReadAllBytes(path, ct, out _);
        if (!TryDecodeManaged(bytes, path, out var decoded, out _, out _))
        {
            return Fallback.DecodeThumbnail(path, maxDimension, ct);
        }

        ct.ThrowIfCancellationRequested();

        // TGA has no embedded/native scaled decode, so decode full then downscale.
        return ThumbnailScaler.ResizeToThumbnail(ToSkBitmap(decoded), maxDimension);
    }

    /// <summary>
    /// Attempts a managed TGA decode, as far as a short file goes. Returns false (signaling the caller
    /// to fall back to the content-sniffing decoder) when the bytes are not a TGA, with no
    /// <paramref name="failure"/>, or when they look like one but cannot be decoded, with it.
    /// </summary>
    private static bool TryDecodeManaged(byte[] bytes, string path, out DecodedImage decoded, out bool truncated, out Exception? failure)
    {
        decoded = default;
        truncated = false;
        failure = null;

        if (!TgaReader.CanDecode(bytes))
        {
            Logger.Warning($"[TgaDecoder] {path} does not look like a TGA (extension mismatch); falling back to {nameof(SkiaDecoder)}.");
            return false;
        }

        try
        {
            decoded = TgaReader.Decode(bytes, out truncated);
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Logger.Warning($"[TgaDecoder] Managed TGA decode failed for {path} ({e.Message}); falling back to {nameof(SkiaDecoder)}.");
            failure = e;
            return false;
        }
    }

    private static string Reason(Exception failure)
    {
        var message = failure.Message.StartsWith("TGA: ", StringComparison.Ordinal) ? failure.Message[5..] : failure.Message;
        return message.Length > 0 ? char.ToUpperInvariant(message[0]) + message[1..] : message;
    }

    private static void Describe(Composite composite, ReadOnlySpan<byte> data)
    {
        var header = TgaReader.ReadHeader(data);
        var type = header.ImageType;

        composite.AddFormatSpecific("Format", TgaReader.IsVersion2(data) ? "TGA 2.0" : "TGA 1.0");
        composite.AddFormatSpecific("Image Type", type.IsColorMapped() ? "Color-mapped" : type.IsGrayscale() ? "Grayscale" : "True color");
        composite.AddFormatSpecific("Compression", type.IsRunLengthEncoded() ? "RLE" : "None");
        composite.AddFormatSpecific("Bit Depth", type.IsColorMapped() ? $"{header.PixelDepth}-bit indices" : $"{header.PixelDepth}-bit");

        if (type.IsColorMapped())
            composite.AddFormatSpecific("Palette", $"{header.CMapLength} colors, {header.CMapDepth}-bit");

        composite.AddFormatSpecific("Has Alpha", header.HasAlpha);
        composite.AddFormatSpecific("Origin", header.Origin switch
        {
            TgaImageOrigin.BottomLeft => "Bottom left",
            TgaImageOrigin.BottomRight => "Bottom right",
            TgaImageOrigin.TopLeft => "Top left",
            _ => "Top right"
        });

        if (ImageId(data, header) is { } id)
            composite.AddFormatSpecific("Image ID", id);
    }

    /// <summary>The optional ID field after the header, when it is text; encoders also use it for binary data.</summary>
    private static string? ImageId(ReadOnlySpan<byte> data, TgaHeader header)
    {
        if (header.IdLength == 0 || data.Length < TgaHeader.Size + header.IdLength)
            return null;

        var field = data.Slice(TgaHeader.Size, header.IdLength).TrimEnd((byte)0);
        if (field.IsEmpty || field.ContainsAnyExceptInRange((byte)0x20, (byte)0x7E))
            return null;

        return Encoding.ASCII.GetString(field).Trim() is { Length: > 0 } text ? text : null;
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