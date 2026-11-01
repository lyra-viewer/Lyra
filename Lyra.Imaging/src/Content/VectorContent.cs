using SkiaSharp;

namespace Lyra.Imaging.Content;

public sealed class VectorContent : ICompositeContent
{
    public VectorContent(SKPicture picture)
    {
        Picture = picture ?? throw new ArgumentNullException(nameof(picture));
        _bounds = picture.CullRect;
    }

    public bool IsResolutionIndependent => true;

    public SKPicture Picture { get; }

    private readonly SKRect _bounds;
    private int _disposed;

    public float? DecodedWidth => _bounds.Width;
    public float? DecodedHeight => _bounds.Height;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            Picture.Dispose();
    }
}