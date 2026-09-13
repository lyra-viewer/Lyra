using SkiaSharp;

namespace Lyra.Imaging.Content;

public class RasterContent : ICompositeContent
{
    public RasterContent(SKImage image)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
    }

    public RasterContent(SKBitmap backingBitmap, SKImage image)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        _backingBitmap = backingBitmap ?? throw new ArgumentNullException(nameof(backingBitmap));
    }

    public bool IsResolutionIndependent => false;

    public SKImage Image { get; }
    
    private readonly SKBitmap? _backingBitmap;
    
    public float? DecodedWidth => Image.Width;
    public float? DecodedHeight => Image.Height;

    public long ByteSize => (long)Image.Width * Image.Height * Math.Max(1, Image.ColorType.GetBytesPerPixel());

    public void Dispose()
    {
        if (Image.Handle != IntPtr.Zero)
            Image.Dispose();

        if (_backingBitmap is not null && _backingBitmap.Handle != IntPtr.Zero)
            _backingBitmap.Dispose();
    }
}