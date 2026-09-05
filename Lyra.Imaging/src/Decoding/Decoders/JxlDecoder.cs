using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Metadata;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Decoding.Decoders;

internal class JxlDecoder : IImageDecoder
{
    private static readonly SKColorSpace SdrColorSpace =
        SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);

    public bool CanDecode(ImageFormatType format) => format == ImageFormatType.Jxl;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[JxlDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        var data = DecoderIO.ReadAllBytes(path, ct, out var readMs, composite.ReportTransferred);
        composite.CompleteTransfer(data.Length, readMs);

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

            return Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[JxlDecoder] Failed to load {path}: {ex.Message}");
            throw;
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
        // Native hands back tightly packed, straight-alpha RGBA8. Premultiply
        // into the bitmap, matching the proven raster path used by J2KDecoder.
        var srcStride = width * 4;

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SdrColorSpace);
        var bitmap = new SKBitmap(info);
        bitmap.Erase(SKColors.Transparent);

        var dst = (byte*)bitmap.GetPixels();
        var dstStride = bitmap.Info.RowBytes;

        for (var y = 0; y < height; y++)
        {
            if ((y & 0x3F) == 0)
                ct.ThrowIfCancellationRequested();

            var srcRow = src + (nint)y * srcStride;
            var dstRow = dst + (nint)y * dstStride;

            for (var x = 0; x < width; x++)
            {
                var i = x * 4;

                var r = srcRow[i + 0];
                var g = srcRow[i + 1];
                var b = srcRow[i + 2];
                var a = srcRow[i + 3];

                if (a == 0)
                {
                    dstRow[i + 0] = 0;
                    dstRow[i + 1] = 0;
                    dstRow[i + 2] = 0;
                    dstRow[i + 3] = 0;
                }
                else if (a == 255)
                {
                    dstRow[i + 0] = r;
                    dstRow[i + 1] = g;
                    dstRow[i + 2] = b;
                    dstRow[i + 3] = 255;
                }
                else
                {
                    dstRow[i + 0] = (byte)((r * a + 127) / 255);
                    dstRow[i + 1] = (byte)((g * a + 127) / 255);
                    dstRow[i + 2] = (byte)((b * a + 127) / 255);
                    dstRow[i + 3] = a;
                }
            }
        }

        return RasterContentBuilder.Build(bitmap, composite);
    }
}