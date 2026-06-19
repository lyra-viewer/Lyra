using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using SkiaSharp;
using static System.Threading.Thread;
using Lyra.Imaging.Decoding.Support;

namespace Lyra.Imaging.Decoding.Decoders;

internal abstract class FloatRgbaDecoderBase : IImageDecoder
{
    public abstract bool CanDecode(ImageFormatType format);

    /// <summary>
    /// Loads RGBA float pixels for <paramref name="path"/>. Implementations throw on failure;
    /// the returned buffer is owned by this base and released via its <c>Dispose</c>.
    /// </summary>
    protected abstract FloatImageBuffer LoadPixels(string path);

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        composite.DecoderName = GetType().Name;
        var path = composite.FileInfo.FullName;
        Logger.Debug($"[{GetType().Name}] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        ct.ThrowIfCancellationRequested();

        using var pixels = LoadPixels(path);

        ct.ThrowIfCancellationRequested();

        var width = pixels.Width;
        var height = pixels.Height;
        DecoderValidation.RequireSaneDimensions(GetType().Name, width, height, bytesPerPixel: sizeof(float) * 4);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);

        bool isGrayscale;
        unsafe
        {
            HdrToneMap.ToBitmap(pixels.AsSpan(), bitmap, ct, out isGrayscale);
        }

        composite.FormatSpecific["GrayScale"] = isGrayscale.ToString();

        ct.ThrowIfCancellationRequested();

        bitmap.SetImmutable();
        var image = SKImage.FromBitmap(bitmap);

        composite.Content = new RasterContent(bitmap, image);

        return Task.CompletedTask;
    }
}