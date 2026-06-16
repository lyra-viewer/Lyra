using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Pipeline;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Decoding.Decoders;

internal class ImageSharpDecoder : IImageDecoder, IThumbnailDecoder
{
    public bool CanDecode(ImageFormatType format) => format
        is ImageFormatType.Tga
        or ImageFormatType.Tiff;

    // public bool CanDecode(ImageFormatType format) => format 
    //     is ImageFormatType.Bmp
    //     or ImageFormatType.Jfif
    //     or ImageFormatType.Jpeg
    //     or ImageFormatType.Png
    //     or ImageFormatType.Tga 
    //     or ImageFormatType.Tiff
    //     or ImageFormatType.Webp;

    public async Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[ImageSharpDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        composite.ExifInfo = MetadataProcessor.ParseMetadata(path);

        try
        {
            ct.ThrowIfCancellationRequested();

            using var image = await Image.LoadAsync<Rgba32>(path, ct);

            var bitmap = ToSkBitmap(image);

            ct.ThrowIfCancellationRequested();

            bitmap.SetImmutable();
            var skImage = SKImage.FromBitmap(bitmap);
            composite.Content = new RasterContent(bitmap, skImage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.Warning($"[ImageSharpDecoder] Image could not be loaded: {path}\n{e.Message}");
            throw;
        }
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var options = new DecoderOptions { TargetSize = new Size(maxDimension, maxDimension) };
        using var image = Image.Load<Rgba32>(options, path);

        ct.ThrowIfCancellationRequested();
        return ToSkBitmap(image);
    }

    private static SKBitmap ToSkBitmap(Image<Rgba32> image)
    {
        var width = image.Width;
        var height = image.Height;

        var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(imageInfo);

        unsafe
        {
            var ptr = (byte*)bitmap.GetPixels().ToPointer();
            var span = new Span<Rgba32>(ptr, width * height);
            image.CopyPixelDataTo(span);
        }

        return bitmap;
    }
}