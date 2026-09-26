using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Metadata;
using SkiaSharp;
using Lyra.Imaging.Decoding.Support;

namespace Lyra.Imaging.Decoding.Decoders;

internal class J2KDecoder : DecoderBase
{
    public override bool CanDecode(ImageFormatType format) => format is ImageFormatType.Jp2 or ImageFormatType.J2k;
    
    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        var data = composite.ReadAllBytes(ct);

        J2KHeader.RequireDeclaredSizeFits(data);

        // OpenJPEG hands back pixels only; JP2 keeps EXIF and XMP in top-level uuid boxes.
        var metadata = IsoBoxMetadata.ReadJp2(data);
        if (!metadata.IsEmpty)
            composite.ExifInfo = MetadataProcessor.ParseMetadata(metadata.Exif, metadata.Xmp, path);

        ct.ThrowIfCancellationRequested();

        IntPtr nativePixels = IntPtr.Zero;
        IntPtr nativeIcc = IntPtr.Zero;

        try
        {
            unsafe
            {
                fixed (byte* pData = data)
                {
                    // Preview knob:
                    // 0 = full res
                    // 1 = half
                    // 2 = quarter
                    const int reduce = 0;

                    var ok = J2KNative.decode_j2k_rgba8_from_memory(
                        (IntPtr)pData,
                        (nuint)data.Length,
                        reduce,
                        out nativePixels,
                        out var width,
                        out var height,
                        out var nativeStrideBytes,
                        out nativeIcc,
                        out var iccSize);

                    if (!ok || nativePixels == IntPtr.Zero)
                    {
                        throw NativeErrors.DecodeFailed(J2KNative.get_last_j2k_error(), path);
                    }

                    DecoderValidation.RequireSaneDimensions(width, height);
                    DecoderValidation.RequireValidStride(nativeStrideBytes, width);

                    composite.ReportPixelCount(width, height);

                    ct.ThrowIfCancellationRequested();

                    var colorSpace = IccColorSpace.FromNative(nativeIcc, iccSize, nameof(J2KDecoder));
                    var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace);
                    var bitmap = new SKBitmap(info);

                    bool isGrayscale;
                    try
                    {
                        PixelCopy.CopyPremultiplyingRgba(nativePixels, nativeStrideBytes, bitmap, ct, out isGrayscale);
                    }
                    catch
                    {
                        bitmap.Dispose();
                        throw;
                    }

                    composite.AddFormatSpecific("GrayScale", isGrayscale.ToString());

                    composite.Content = RasterContentBuilder.Build(bitmap, composite);
                }
            }
        }
        finally
        {
            if (nativePixels != IntPtr.Zero)
                J2KNative.free_j2k_pixels(nativePixels);
            if (nativeIcc != IntPtr.Zero)
                J2KNative.free_j2k_pixels(nativeIcc);
        }
    }
}