using SkiaSharp;

namespace Lyra.UI.Components.Primitives;

public class Image : ImageBase
{
    public SKImage? Source { get; set; }
    public Action<SKCanvas, SKRect>? DrawCustom { get; set; }

    public Image()
    {
    }

    public Image(float width, float height)
    {
        ImageWidth = width;
        ImageHeight = height;
    }

    protected override void RenderContent(SKCanvas canvas, SKRect contentBounds)
    {
        if (Source != null)
            canvas.DrawImage(Source, contentBounds);
        else
            DrawCustom?.Invoke(canvas, contentBounds);
    }

    // No Dispose override needed.
    // SKImage lifetime is managed by whoever created it.
}