using System.Xml;
using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;
using Svg.Skia;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class SvgDecoder : DecoderBase, IThumbnailDecoder
{
    public override IReadOnlyList<ImageFormatType> Formats { get; } = [ImageFormatType.Svg];

    protected override void Decode(DecodeContext context, CancellationToken ct)
    {
        var path = context.Source.Label;

        ct.ThrowIfCancellationRequested();

        var svg = new SKSvg();
        using (var stream = new MeasuredReadStream(context.OpenSequentialRead(), context.ReportTransferred, context.CompleteTransfer))
        {
            try
            {
                svg.Load(stream);
            }
            catch (XmlException e)
            {
                throw new LoadFailureException(LoadFailureKind.DecodeFailed, $"The SVG is not well-formed XML: {e.Message}", e);
            }
            catch (Exception e) when (e is not (OperationCanceledException or OutOfMemoryException or IOException or UnauthorizedAccessException))
            {
                Logger.Warning(typeof(SvgDecoder), $"The SVG library could not read {path}: {e}");
                throw new LoadFailureException(LoadFailureKind.DecodeFailed, $"The SVG could not be read ({e.Message})", e);
            }
        }

        ct.ThrowIfCancellationRequested();

        var picture = svg.Picture;
        if (picture == null)
        {
            Logger.Warning($"[SvgDecoder] SVG picture is null: {path}");
            return;
        }

        var originalBounds = picture.CullRect;
        if (originalBounds.IsEmpty || originalBounds.Width < 1 || originalBounds.Height < 1)
            Logger.Debug($"[SvgDecoder] Detected empty or invalid CullRect: {path}");

        context.Content = new VectorContent(picture);
    
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var svg = new SKSvg();
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