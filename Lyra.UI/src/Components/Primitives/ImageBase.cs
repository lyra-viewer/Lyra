using SkiaSharp;

namespace Lyra.UI.Components.Primitives;

public abstract class ImageBase : ComponentBase
{
    private float _imageWidth;
    private float _imageHeight;

    public float ImageWidth
    {
        get => _imageWidth;
        set => Set(ref _imageWidth, value);
    }

    public float ImageHeight
    {
        get => _imageHeight;
        set => Set(ref _imageHeight, value);
    }

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        if (ImageWidth <= 0f || ImageHeight <= 0f)
            return SKSize.Empty;

        var scale = 1f;

        if (availableSize.Width > 0f && ImageWidth > availableSize.Width)
            scale = availableSize.Width / ImageWidth;

        if (availableSize.Height > 0f && ImageHeight * scale > availableSize.Height)
            scale = availableSize.Height / ImageHeight;

        return new SKSize(ImageWidth * scale, ImageHeight * scale);
    }

    protected override void ArrangeContent(SKRect contentBounds) { }
}