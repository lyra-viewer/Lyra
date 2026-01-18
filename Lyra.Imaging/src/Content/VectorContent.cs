using SkiaSharp;

namespace Lyra.Imaging.Content;

public sealed class VectorContent(SKPicture picture) : ICompositeContent
{
    public CompositeContentKind Kind => CompositeContentKind.Vector;

    public SKPicture Picture { get; private set; } = picture ?? throw new ArgumentNullException(nameof(picture));

    public float? DecodedWidth => Picture.CullRect.Width;
    public float? DecodedHeight => Picture.CullRect.Height;

    public void Dispose() => Picture.Dispose();
}