using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Metadata;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

internal class JxlDecoder : DecoderBase
{
    private static readonly SKColorSpace SdrColorSpace =
        SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);

    public override bool CanDecode(ImageFormatType format) => format == ImageFormatType.Jxl;

    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        var data = composite.ReadAllBytes(ct);

        var metadata = IsoBoxMetadata.ReadJxl(data);
        if (!metadata.IsEmpty)
            composite.ExifInfo = MetadataProcessor.ParseMetadata(metadata.Exif, metadata.Xmp, path);

        ct.ThrowIfCancellationRequested();

        var nativePixels = IntPtr.Zero;

        try
        {
            unsafe
            {
                fixed (byte* pData = data)
                {
                    var ok = JxlNative.decode_jxl_from_memory(
                        (IntPtr)pData,
                        (nuint)data.Length,
                        out var width,
                        out var height,
                        out var isHdr,
                        out var bitsPerSample,
                        out var hasAlpha,
                        out var hasAnimation,
                        out nativePixels);

                    if (!ok || nativePixels == IntPtr.Zero)
                    {
                        var err = NativeErrors.GetUtf8ZOrAnsiZ(JxlNative.get_last_jxl_error());
                        Logger.Error($"[JxlDecoder] Native decode failed: {err}");
                        throw new InvalidOperationException($"[JxlDecoder] Failed to decode: {path}");
                    }

                    DecoderValidation.RequireSaneDimensions("JxlDecoder", width, height);

                    composite.ReportPixelCount(width, height);

                    ct.ThrowIfCancellationRequested();

                    composite.AddFormatSpecific("Bit Depth", $"{bitsPerSample}-bit");
                    composite.AddFormatSpecific("HDR", (isHdr != 0).ToString());
                    composite.AddFormatSpecific("Alpha", (hasAlpha != 0).ToString());
                    composite.AddFormatSpecific("Animated", (hasAnimation != 0).ToString());

                    composite.Content = isHdr != 0
                        ? BuildHdr((byte*)nativePixels, width, height, composite, ct)
                        : BuildSdr((byte*)nativePixels, width, height, composite, ct);
                }
            }
        }
        finally
        {
            if (nativePixels != IntPtr.Zero)
                JxlNative.free_jxl_pixels(nativePixels);
        }
    }

    private static unsafe ICompositeContent BuildHdr(byte* src, int width, int height, Composite composite, CancellationToken ct)
    {
        var rgba = new Span<float>(src, checked(width * height * 4));
        var content = HdrImageBuilder.Build(rgba, width, height, composite, ct, out var isGrayscale);
        composite.AddFormatSpecific("GrayScale", isGrayscale.ToString());

        ct.ThrowIfCancellationRequested();

        return content;
    }

    private static unsafe ICompositeContent BuildSdr(byte* src, int width, int height, Composite composite, CancellationToken ct)
    {
        // Native hands back tightly packed, straight-alpha RGBA8.
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SdrColorSpace);
        var bitmap = new SKBitmap(info);

        PixelCopy.CopyPremultiplyingRgba((IntPtr)src, width * 4, bitmap, ct, out var isGrayscale);

        composite.AddFormatSpecific("GrayScale", isGrayscale.ToString());

        return RasterContentBuilder.Build(bitmap, composite);
    }
}
