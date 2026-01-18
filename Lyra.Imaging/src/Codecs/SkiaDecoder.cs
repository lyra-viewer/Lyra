using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Codecs;

internal class SkiaDecoder : IImageDecoder
{
    public bool CanDecode(ImageFormatType format) => format
        is ImageFormatType.Bmp
        or ImageFormatType.Ico
        or ImageFormatType.Jfif
        or ImageFormatType.Jpeg
        or ImageFormatType.Png
        or ImageFormatType.Webp;

    public async Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        await Task.Yield();

        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[SkiaDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        ct.ThrowIfCancellationRequested();

        await using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.SequentialScan);

        using var bitmap = SKBitmap.Decode(file);
        if (bitmap is null)
        {
            Logger.Warning("[SkiaDecoder] SKBitmap is null.");
            return;
        }

        ct.ThrowIfCancellationRequested();

        var image = SKImage.FromBitmap(bitmap);
        if (image is null)
        {
            Logger.Warning("[SkiaDecoder] SKImage is null.");
            return;
        }

        composite.Content = new RasterContent(image);
    }
}