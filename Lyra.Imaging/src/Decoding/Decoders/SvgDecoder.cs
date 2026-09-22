using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;
using Svg.Skia;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class SvgDecoder : DecoderBase, IThumbnailDecoder
{
    public override bool CanDecode(ImageFormatType format) => format is ImageFormatType.Svg;

    protected override void Decode(Composite composite, string path, CancellationToken ct)
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
            return;
        }

        var originalBounds = picture.CullRect;
        if (originalBounds.IsEmpty || originalBounds.Width < 1 || originalBounds.Height < 1)
            Logger.Debug($"[SvgDecoder] Detected empty or invalid CullRect: {path}");

        composite.Content = new VectorContent(picture);
    
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