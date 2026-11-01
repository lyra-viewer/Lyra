using SkiaSharp;

namespace Lyra.Imaging.Content;

public class RasterContent : ICompositeContent
{
    public RasterContent(SKImage image)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        _width = image.Width;
        _height = image.Height;
        _byteSize = ImageBytes.Of(image);
    }

    public RasterContent(SKBitmap backingBitmap, SKImage image) : this(image)
    {
        _backingBitmap = backingBitmap ?? throw new ArgumentNullException(nameof(backingBitmap));
    }

    public bool IsResolutionIndependent => false;

    public SKImage Image { get; }

    private readonly SKBitmap? _backingBitmap;
    private readonly int _width;
    private readonly int _height;
    private readonly long _byteSize;
    private int _disposed;

    public float? DecodedWidth => _width;
    public float? DecodedHeight => _height;

    public long ByteSize => Volatile.Read(ref _disposed) == 0 ? _byteSize : 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        DisposeCore();
    }

    protected virtual void DisposeCore()
    {
        Image.Dispose();
        _backingBitmap?.Dispose();
    }
}