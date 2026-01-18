using SkiaSharp;

namespace Lyra.Imaging.Content;

public sealed class RasterContent(SKImage image) : ICompositeContent
{
    public CompositeContentKind Kind => CompositeContentKind.Raster;

    public SKImage Image { get; private set; } = image ?? throw new ArgumentNullException(nameof(image));

    public float? DecodedWidth => Image.Width;
    public float? DecodedHeight => Image.Height;

    public void Dispose()
    {
        if (Image.Handle != IntPtr.Zero)
            Image.Dispose();
    }
}