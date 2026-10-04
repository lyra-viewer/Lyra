using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.ManagedCodecs.Raster.Qoi;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

/// <summary>
/// QOI, decoded by the managed reader straight into the bitmap that is displayed. The format
/// carries no metadata, so there is none to read. A file that ends early is shown as far as
/// it goes, with a warning.
/// </summary>
internal sealed class QoiDecoder : DecoderBase, IThumbnailDecoder
{
    // Routing is by extension. A file named *.qoi without the QOI signature goes to the
    // content-sniffing Skia decoder, which reads whatever it really is.
    private static readonly SkiaDecoder Fallback = new();

    public override bool CanDecode(ImageFormatType format) => format is ImageFormatType.Qoi;

    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        var bytes = composite.ReadAllBytes(ct);

        if (!QoiReader.CanDecode(bytes))
        {
            Logger.Warning($"[QoiDecoder] {path} has no QOI signature; falling back to {nameof(SkiaDecoder)}.");
            Fallback.DecodeAsync(composite, ct).GetAwaiter().GetResult();
            return;
        }

        var header = QoiReader.ReadHeader(bytes);

        Describe(composite, header);
        composite.ReportPixelCount(header.Width, header.Height);

        var bitmap = DecodeBitmap(bytes, header, ct, out var decoded);

        if (decoded < header.PixelCount)
        {
            var why = $"The file ends after {decoded} of its {header.PixelCount} pixels";

            Logger.Warning($"[QoiDecoder] {path}: {why}; shown as far as it goes, the rest left transparent.");
            composite.Warning = LoadWarning.PartiallyDecoded(why);
        }

        composite.Content = RasterContentBuilder.Build(bitmap, composite);
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var bytes = DecoderIO.ReadAllBytes(path, ct, out _);
        if (!QoiReader.CanDecode(bytes))
            return Fallback.DecodeThumbnail(path, maxDimension, ct);

        var header = QoiReader.ReadHeader(bytes);

        // QOI has no scaled decode, so the whole image is decoded and then reduced.
        return ThumbnailScaler.ResizeToThumbnail(DecodeBitmap(bytes, header, ct, out _), maxDimension);
    }

    private static unsafe SKBitmap DecodeBitmap(byte[] bytes, QoiHeader header, CancellationToken ct, out long decoded)
    {
        if (header.PixelCount > QoiReader.MaxPixels)
            throw new LoadFailureException(LoadFailureKind.TooLarge, $"{header.Width}x{header.Height} exceeds the {QoiReader.MaxPixels / 1_000_000} megapixel limit of the format's reference decoder");

        var (width, height) = ((int)header.Width, (int)header.Height);

        DecoderValidation.RequireSaneDimensions(width, height);
        DecoderValidation.RequireAvailableMemory(width, height);

        var colorSpace = header.ColorSpace == QoiColorSpace.Linear ? SKColorSpace.CreateSrgbLinear() : SKColorSpace.CreateSrgb();
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul, colorSpace));

        try
        {
            if (bitmap.GetPixels() == IntPtr.Zero)
                throw new OutOfMemoryException($"Could not allocate a {width}x{height} bitmap.");

            var tight = width * 4;
            if (bitmap.RowBytes == tight)
            {
                decoded = QoiReader.DecodeInto(bytes, header, new Span<byte>((void*)bitmap.GetPixels(), tight * height), ct);
            }
            else
            {
                var rgba = new byte[(long)tight * height];
                decoded = QoiReader.DecodeInto(bytes, header, rgba, ct);
                PixelCopy.CopyTightRgba(rgba, bitmap);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void Describe(Composite composite, QoiHeader header)
    {
        composite.AddFormatSpecific("Format", "QOI");
        composite.AddFormatSpecific("Channels", header.Channels == 4 ? "RGBA" : "RGB");
        composite.AddFormatSpecific("Color Space", header.ColorSpace == QoiColorSpace.Linear ? "linear" : "sRGB, linear alpha");
    }
}
