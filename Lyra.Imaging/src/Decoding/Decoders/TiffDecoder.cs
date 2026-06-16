using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Pipeline;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Decoding.Decoders;

/// <summary>
/// Decodes TIFF images via the native libtiff wrapper. libtiff handles every compression,
/// photometric, planar and tiled variant and returns an 8-bit RGBA raster with
/// <em>premultiplied</em> (associated) alpha, top-left origin.
/// </summary>
internal sealed class TiffDecoder : IImageDecoder, IThumbnailDecoder
{
    public bool CanDecode(ImageFormatType format) => format is ImageFormatType.Tiff;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[TiffDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        composite.ExifInfo = MetadataProcessor.ParseMetadata(path);

        try
        {
            ct.ThrowIfCancellationRequested();

            var bitmap = LoadBitmap(path, ct);
            bitmap.SetImmutable();
            var image = SKImage.FromBitmap(bitmap);
            composite.Content = new RasterContent(bitmap, image);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.Warning($"[TiffDecoder] Image could not be loaded: {path}\n{e.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var full = LoadBitmap(path, ct);

        // libtiff has no native scaled decode, so decode full then downscale. The caller does the
        // final downscale to the hash grid, so a single linear pass here is enough.
        var longestSide = Math.Max(full.Width, full.Height);
        if (longestSide <= maxDimension)
        {
            return full;
        }

        var scale = (float)maxDimension / longestSide;
        var targetWidth = Math.Max(1, (int)MathF.Round(full.Width * scale));
        var targetHeight = Math.Max(1, (int)MathF.Round(full.Height * scale));

        using (full)
        {
            var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            return full.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }
    }

    private static SKBitmap LoadBitmap(string path, CancellationToken ct)
    {
        if (!TiffNative.load_tiff_rgba(path, out var ptr, out var width, out var height) || ptr == IntPtr.Zero)
        {
            var error = NativeErrors.GetUtf8ZOrAnsiZ(TiffNative.get_last_tiff_error());
            throw new InvalidOperationException($"[TiffDecoder] Failed to decode TIFF: {path}. {error}");
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), width, height);

            var byteCount = checked(width * height * 4);
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var bitmap = new SKBitmap(info);

            unsafe
            {
                var src = new Span<byte>((void*)ptr, byteCount);
                var dst = new Span<byte>((void*)bitmap.GetPixels(), byteCount);
                src.CopyTo(dst);
            }

            return bitmap;
        }
        finally
        {
            TiffNative.free_tiff_pixels(ptr);
        }
    }
}