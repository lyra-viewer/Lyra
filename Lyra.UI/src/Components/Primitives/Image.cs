using SkiaSharp;

namespace Lyra.UI.Components.Primitives;

public class Image : ImageBase
{
    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

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
            canvas.DrawImage(Source, contentBounds, LinearSampling);
        else
            DrawCustom?.Invoke(canvas, contentBounds);
    }

    // No Dispose override needed.
    // SKImage lifetime is managed by whoever created it.
}