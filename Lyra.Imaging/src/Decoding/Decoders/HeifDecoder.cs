using LibHeifSharp;
using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Pipeline;
using SkiaSharp;
using static System.Threading.Thread;
using Lyra.Imaging.Decoding.Support;

namespace Lyra.Imaging.Decoding.Decoders;

internal class HeifDecoder : IImageDecoder, IThumbnailDecoder
{
    public bool CanDecode(ImageFormatType format) => format == ImageFormatType.Heif;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[HeifDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        try
        {
            ct.ThrowIfCancellationRequested();

            using var heifContext = new HeifContext(path);
            using var imageHandle = heifContext.GetPrimaryImageHandle();

            PopulateFormatSpecific(composite, heifContext, imageHandle, path);

            // Decode as 8-bit RGBA interleaved.
            using var decodedImage = imageHandle.Decode(HeifColorspace.Rgb, HeifChroma.InterleavedRgba32);

            // EXIF
            var exifData = imageHandle.GetExifMetadata();
            if (exifData != null)
            {
                using var stream = new MemoryStream(exifData);
                composite.ExifInfo = MetadataProcessor.ParseMetadata(stream, path);
            }

            var bitmap = DecodedImageToBitmap(decodedImage, ct);

            ct.ThrowIfCancellationRequested();

            bitmap.SetImmutable();
            var skImage = SKImage.FromBitmap(bitmap);
            composite.Content = new RasterContent(bitmap, skImage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HeifException e)
        {
            // Expected for some HEIF variants (e.g. 'unci'); keep as warning-only.
            Logger.Warning($"[HeifDecoder] Unsupported HEIF feature for file: {path}\n{e.Message}");
        }
        catch (Exception e)
        {
            Logger.Warning($"[HeifDecoder] Image could not be loaded: {path}\n{e.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var heifContext = new HeifContext(path);
        using var primaryHandle = heifContext.GetPrimaryImageHandle();

        var embedded = TryGetEmbeddedThumbnail(primaryHandle, maxDimension);
        try
        {
            var handle = embedded ?? primaryHandle;
            using var decodedImage = handle.Decode(HeifColorspace.Rgb, HeifChroma.InterleavedRgba32);

            ct.ThrowIfCancellationRequested();
            return DecodedImageToBitmap(decodedImage, ct);
        }
        finally
        {
            embedded?.Dispose();
        }
    }
    
    private static HeifImageHandle? TryGetEmbeddedThumbnail(HeifImageHandle handle, int maxDimension)
    {
        try
        {
            var ids = handle.GetThumbnailImageIds();
            if (ids is not { Count: > 0 })
                return null;

            foreach (var id in ids)
            {
                var thumb = handle.GetThumbnailImage(id);
                if (Math.Max(thumb.Width, thumb.Height) >= maxDimension)
                    return thumb;

                thumb.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"[HeifDecoder] Embedded thumbnail lookup failed: {ex.Message}");
        }

        return null;
    }

    private static SKBitmap DecodedImageToBitmap(HeifImage decodedImage, CancellationToken ct)
    {
        var width = decodedImage.Width;
        var height = decodedImage.Height;

        var plane = decodedImage.GetPlane(HeifChannel.Interleaved);
        var src = plane.Scan0;
        if (src == IntPtr.Zero)
            throw new InvalidOperationException("HEIF decode returned null interleaved plane.");

        var srcStride = plane.Stride;

        DecoderValidation.RequireSaneDimensions("HeifDecoder", width, height);
        DecoderValidation.RequireValidStride("HeifDecoder", srcStride, width);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);

        unsafe
        {
            var dstSpan = bitmap.GetPixelSpan();
            fixed (void* dstBase = &dstSpan.GetPinnableReference())
            {
                byte* srcBase = (byte*)src;
                byte* dst = (byte*)dstBase;

                const int bytesPerPixel = 4;
                var rowBytes = width * bytesPerPixel;

                var dstStride = bitmap.RowBytes;

                var copyBytes = Math.Min(rowBytes, Math.Min(srcStride, dstStride));

                for (var y = 0; y < height; y++)
                {
                    ct.ThrowIfCancellationRequested();

                    var srcRow = srcBase + (nint)y * (nint)srcStride;
                    var dstRow = dst + (nint)y * (nint)dstStride;

                    Buffer.MemoryCopy(srcRow, dstRow, dstStride, copyBytes);
                }
            }
        }

        return bitmap;
    }

    private static void PopulateFormatSpecific(Composite composite, HeifContext context, HeifImageHandle handle, string path)
    {
        composite.FormatSpecific["Codec"] = DetectHeifBrand(path);

        // TryAdd(composite, "Bit Depth", () => $"{handle.LumaBitsPerPixel}-bit");
        TryAdd(composite, "Has Alpha", () => handle.HasAlphaChannel.ToString());
        TryAdd(composite, "Depth Map", () => handle.HasDepthImage ? "Yes" : "No");

        try
        {
            var thumbs = handle.GetThumbnailImageIds();
            if (thumbs is { Count: > 0 })
                composite.FormatSpecific["Thumbnails"] = thumbs.Count.ToString();
        }
        catch (Exception ex)
        {
            Logger.Debug($"[HeifDecoder] Thumbnail enumeration failed: {ex.Message}");
        }

        try
        {
            var topIds = context.GetTopLevelImageIds();
            if (topIds is { Count: > 1 })
                composite.FormatSpecific["Top-level Images"] = topIds.Count.ToString();
        }
        catch (Exception ex)
        {
            Logger.Debug($"[HeifDecoder] Top-level enumeration failed: {ex.Message}");
        }
    }

    private static void TryAdd(Composite composite, string key, Func<string> producer)
    {
        try
        {
            composite.FormatSpecific[key] = producer();
        }
        catch (Exception ex)
        {
            Logger.Debug($"[HeifDecoder] FormatSpecific '{key}' failed: {ex.Message}");
        }
    }

    private static string DetectHeifBrand(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> buf = stackalloc byte[32];
            var read = fs.Read(buf);
            if (read < 12)
                return "Unknown";

            // ISOBMFF ftyp box layout:
            //   [0..4)  box size
            //   [4..8)  "ftyp"
            //   [8..12) major brand
            var brand = System.Text.Encoding.ASCII.GetString(buf.Slice(8, 4));
            return brand switch
            {
                "heic" or "heix" or "heim" or "heis" => "HEVC",
                "avif" or "avis" => "AV1",
                "jpeg" or "jpgs" => "JPEG",
                "mif1" or "msf1" => "HEIF (generic)",
                _ => brand
            };
        }
        catch
        {
            return "Unknown";
        }
    }
}