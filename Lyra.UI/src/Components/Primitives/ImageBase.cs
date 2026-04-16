using SkiaSharp;

namespace Lyra.UI.Components.Primitives;

public abstract class ImageBase : ComponentBase
{
    public float ImageWidth { get; set; }
    public float ImageHeight { get; set; }

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        return new SKSize(ImageWidth, ImageHeight);
    }

    protected override void ArrangeContent(SKRect contentBounds)
    {
    }
}