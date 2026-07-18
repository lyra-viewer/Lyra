using System.Runtime.InteropServices;
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
/// <em>premultiplied</em> (associated) alpha, top-left origin. Unassociated-alpha TIFFs are
/// converted by libtiff itself (the UaToAa table in tif_getimage.c), so Premul is correct
/// for both EXTRASAMPLES variants.
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

            var bitmap = LoadBitmap(path, ct, tagColorSpace: true);
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

        // libtiff has no native scaled decode, so decode full then downscale.
        return ThumbnailScaler.ResizeToThumbnail(LoadBitmap(path, ct, tagColorSpace: false), maxDimension);
    }

    private static SKBitmap LoadBitmap(string path, CancellationToken ct, bool tagColorSpace)
    {
        if (!TiffNative.load_tiff_rgba(path, out var ptr, out var width, out var height, out var iccPtr, out var iccSize)
            || ptr == IntPtr.Zero)
        {
            var error = NativeErrors.GetUtf8ZOrAnsiZ(TiffNative.get_last_tiff_error());
            throw new InvalidOperationException($"[TiffDecoder] Failed to decode TIFF: {path}. {error}");
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), width, height);

            var colorSpace = tagColorSpace ? ResolveIccColorSpace(iccPtr, iccSize) : null;

            var byteCount = checked(width * height * 4);
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace);
            var bitmap = new SKBitmap(info);

            unsafe
            {
                PixelCopy.CopyTightRgba(new ReadOnlySpan<byte>((void*)ptr, byteCount), bitmap);
            }

            return bitmap;
        }
        finally
        {
            TiffNative.free_tiff_pixels(ptr);
            if (iccPtr != IntPtr.Zero)
                TiffNative.free_tiff_pixels(iccPtr);
        }
    }

    private static SKColorSpace? ResolveIccColorSpace(IntPtr iccPtr, int iccSize)
    {
        if (iccPtr == IntPtr.Zero || iccSize <= 0)
            return null;

        try
        {
            var icc = new byte[iccSize];
            Marshal.Copy(iccPtr, icc, 0, iccSize);
            return SKColorSpace.CreateIcc(icc);
        }
        catch (Exception ex)
        {
            Logger.Debug($"[TiffDecoder] ICC profile parse failed: {ex.Message}");
            return null;
        }
    }
}