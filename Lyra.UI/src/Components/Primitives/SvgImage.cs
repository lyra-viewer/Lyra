using SkiaSharp;
using Svg.Skia;

namespace Lyra.UI.Components.Primitives;

public class SvgImage : ImageBase
{
    private SKPicture? _picture;
    private readonly bool _ownsPicture;

    /// Wraps a cached SKPicture from ResourceLoader.
    /// Caller retains ownership - this instance does not dispose the picture.
    public SvgImage(SKPicture picture, float width, float height)
    {
        ImageWidth = width;
        ImageHeight = height;
        _picture = picture;
        _ownsPicture = false;
    }

    /// Loads from a file path. This instance owns and disposes the picture.
    public SvgImage(string svgPath, float width, float height)
    {
        ImageWidth = width;
        ImageHeight = height;
        _ownsPicture = true;

        if (!File.Exists(svgPath))
            return;

        using var stream = File.OpenRead(svgPath);
        LoadSvg(stream);
    }

    /// Loads from a stream. This instance owns and disposes the picture.
    public SvgImage(Stream svgStream, float width, float height)
    {
        ImageWidth = width;
        ImageHeight = height;
        _ownsPicture = true;
        LoadSvg(svgStream);
    }

    private void LoadSvg(Stream stream)
    {
        try
        {
            var svg = new SKSvg();
            _picture = svg.Load(stream);
        }
        catch
        {
            // Malformed SVG
            _picture = null;
        }
    }

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        if (_picture is null)
            return;

        var cull = _picture.CullRect;
        if (cull.Width <= 0 || cull.Height <= 0)
            return;

        var scaleX = contentBounds.Width / cull.Width;
        var scaleY = contentBounds.Height / cull.Height;

        canvas.Save();
        canvas.Translate(contentBounds.Left, contentBounds.Top);
        canvas.Scale(scaleX, scaleY);
        canvas.Translate(-cull.Left, -cull.Top);
        canvas.DrawPicture(_picture);
        canvas.Restore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsPicture)
        {
            _picture?.Dispose();
            _picture = null;
        }

        base.Dispose(disposing);
    }
}