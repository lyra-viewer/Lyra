using LibHeifSharp;
using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Pipeline;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Codecs;

internal class HeifDecoder : IImageDecoder
{
    public bool CanDecode(ImageFormatType format) => format == ImageFormatType.Heif;

    public async Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[HeifDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var heifContext = new HeifContext(path);
                using var imageHandle = heifContext.GetPrimaryImageHandle();

                // Decode as 8-bit RGBA interleaved.
                using var decodedImage = imageHandle.Decode(HeifColorspace.Rgb, HeifChroma.InterleavedRgba32);

                // EXIF
                var exifData = imageHandle.GetExifMetadata();
                if (exifData != null)
                {
                    using var stream = new MemoryStream(exifData);
                    composite.ExifInfo = MetadataProcessor.ParseMetadata(stream, path);
                }

                var width = decodedImage.Width;
                var height = decodedImage.Height;

                var plane = decodedImage.GetPlane(HeifChannel.Interleaved);
                var src = plane.Scan0;
                if (src == IntPtr.Zero)
                    throw new InvalidOperationException("HEIF decode returned null interleaved plane.");

                var srcStride = plane.Stride;

                var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                using var bitmap = new SKBitmap(info);

                unsafe
                {
                    var dstSpan = bitmap.GetPixelSpan();
                    fixed (void* dstBase = &dstSpan.GetPinnableReference())
                    {
                        byte* srcBase = (byte*)src;
                        byte* dst = (byte*)dstBase;

                        var bytesPerPixel = 4;
                        var rowBytes = width * bytesPerPixel;

                        // Destination row stride
                        var dstStride = bitmap.RowBytes;

                        // Safety: never copy more than either row allows
                        var copyBytes = Math.Min(rowBytes, Math.Min(srcStride, dstStride));

                        for (var y = 0; y < height; y++)
                        {
                            ct.ThrowIfCancellationRequested();

                            var srcRow = srcBase + (y * srcStride);
                            var dstRow = dst + (y * dstStride);

                            Buffer.MemoryCopy(srcRow, dstRow, dstStride, copyBytes);

                            // If dstStride > copyBytes, the remaining bytes are left as-is.
                            // That’s fine; Skia will ignore padding.
                        }
                    }
                }

                composite.Content = new RasterContent(SKImage.FromBitmap(bitmap));
            }
            catch (OperationCanceledException)
            {
                // Normal cancel path; don't spam warnings.
                throw;
            }
            catch (HeifException e)
            {
                // Important: this is expected for some libheif test images (e.g. 'unci').
                Logger.Warning($"[HeifDecoder] Unsupported HEIF feature for file: {path}\n{e.Message}");
            }
            catch (Exception e)
            {
                Logger.Warning($"[HeifDecoder] Image could not be loaded: {path}\n{e}");
            }
        }, ct);
    }
}