using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;
using Svg.Skia;
using static System.Threading.Thread;

namespace Lyra.Imaging.Decoding.Decoders;

public class SvgDecoder : IImageDecoder, IThumbnailDecoder
{
    public bool CanDecode(ImageFormatType format) => format is ImageFormatType.Svg;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[SvgDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        try
        {
            ct.ThrowIfCancellationRequested();

            var svg = new SKSvg();
            using (var stream = new MeasuredReadStream(DecoderIO.OpenSequentialRead(path), composite.ReportTransferred, composite.CompleteTransfer))
            {
                svg.Load(stream);
            }

            ct.ThrowIfCancellationRequested();

            var picture = svg.Picture;
            if (picture == null)
            {
                Logger.Warning($"[SvgDecoder] SVG picture is null: {path}");
                return Task.CompletedTask;
            }

            var originalBounds = picture.CullRect;
            if (originalBounds.IsEmpty || originalBounds.Width < 1 || originalBounds.Height < 1)
                Logger.Debug($"[SvgDecoder] Detected empty or invalid CullRect: {path}");

            composite.Content = new VectorContent(picture);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[SvgDecoder] Failed to load {path}: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var svg = new SKSvg();
        svg.Load(path);

        ct.ThrowIfCancellationRequested();

        var picture = svg.Picture;
        if (picture is null)
            return null;

        var bounds = picture.CullRect;
        if (bounds.Width < 1 || bounds.Height < 1)
            return null;

        // Vector renders losslessly at any size: scale so the larger side == maxDimension.
        var scale = maxDimension / Math.Max(bounds.Width, bounds.Height);
        var width = Math.Max(1, (int)MathF.Round(bounds.Width * scale));
        var height = Math.Max(1, (int)MathF.Round(bounds.Height * scale));

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);

        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);

        return bitmap;
    }
}