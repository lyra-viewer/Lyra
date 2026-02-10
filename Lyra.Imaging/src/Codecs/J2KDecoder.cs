using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Interop;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Codecs;

internal sealed class J2KDecoder : IImageDecoder
{
    public bool CanDecode(ImageFormatType format) => format is ImageFormatType.Jp2 or ImageFormatType.J2k;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[J2KDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        var data = File.ReadAllBytes(path);

        ct.ThrowIfCancellationRequested();

        IntPtr nativePixels = IntPtr.Zero;

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
                        out var nativeStrideBytes);

                    if (!ok || nativePixels == IntPtr.Zero)
                    {
                        var err = NativeErrors.GetUtf8ZOrAnsiZ(J2KNative.get_last_j2k_error());
                        Logger.Error($"[J2KDecoder] Native decode failed: {err}");
                        throw new InvalidOperationException($"[J2KDecoder] Failed to decode: {path}");
                    }

                    if (width <= 0 || height <= 0 || nativeStrideBytes <= 0)
                        throw new InvalidOperationException($"[J2KDecoder] Invalid decoded image geometry: {width}x{height}, stride={nativeStrideBytes}");

                    ct.ThrowIfCancellationRequested();

                    var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
                    var bitmap = new SKBitmap(info);
                    bitmap.Erase(SKColors.Transparent);

                    var dst = (byte*)bitmap.GetPixels();
                    var dstStride = bitmap.Info.RowBytes;

                    var src = (byte*)nativePixels;

                    var opaque = IsLikelyOpaque(src, nativeStrideBytes, width, height);

                    composite.IsGrayscale = true;

                    if (opaque)
                    {
                        CopyRows(src, nativeStrideBytes, dst, dstStride, height, Math.Min(nativeStrideBytes, dstStride));

                        UpdateGrayscaleFlag(src, nativeStrideBytes, width, height, ref composite.IsGrayscale);

                        bitmap.SetImmutable();
                        var image = SKImage.FromBitmap(bitmap);
                        composite.Content = new RasterContent(bitmap, image);
                        return Task.CompletedTask;
                    }

                    for (var y = 0; y < height; y++)
                    {
                        if ((y & 0x3F) == 0)
                            ct.ThrowIfCancellationRequested();

                        var srcRow = src + (nint)y * nativeStrideBytes;
                        var dstRow = dst + (nint)y * dstStride;

                        for (var x = 0; x < width; x++)
                        {
                            var i = x * 4;

                            var r = srcRow[i + 0];
                            var g = srcRow[i + 1];
                            var b = srcRow[i + 2];
                            var a = srcRow[i + 3];

                            if (composite.IsGrayscale && (g != r || b != r))
                                composite.IsGrayscale = false;

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

                    bitmap.SetImmutable();
                    var skImage = SKImage.FromBitmap(bitmap);
                    composite.Content = new RasterContent(bitmap, skImage);
                }
            }

            return Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            throw; // propagate cancel to Loader
        }
        catch (Exception ex)
        {
            Logger.Warning($"[J2KDecoder] Failed to load {path}: {ex}");
            throw; // propagate failure to Loader
        }
        finally
        {
            if (nativePixels != IntPtr.Zero)
                J2KNative.free_j2k_pixels(nativePixels);
        }
    }

    private static unsafe bool IsLikelyOpaque(byte* src, int stride, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            var row = src + (nint)y * stride;
            for (var x = 0; x < width; x++)
            {
                if (row[x * 4 + 3] != 255)
                    return false;
            }
        }

        return true;
    }

    private static unsafe void CopyRows(byte* src, int srcStride, byte* dst, int dstStride, int height, int bytesPerRow)
    {
        for (var y = 0; y < height; y++)
        {
            Buffer.MemoryCopy(src + (nint)y * srcStride, dst + (nint)y * dstStride, dstStride, bytesPerRow);
        }
    }

    private static unsafe void UpdateGrayscaleFlag(byte* src, int stride, int width, int height, ref bool isGrayscale)
    {
        if (!isGrayscale)
            return;

        for (var y = 0; y < height; y++)
        {
            var row = src + (nint)y * stride;
            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                var r = row[i + 0];
                var g = row[i + 1];
                var b = row[i + 2];
                if (g != r || b != r)
                {
                    isGrayscale = false;
                    return;
                }
            }
        }
    }
}