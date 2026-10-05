using SkiaSharp;

namespace Lyra.Imaging.Content;

/// <summary>What decoded pixels cost in memory, counted one way everywhere.</summary>
internal static class ImageBytes
{
    public static int PerPixel(SKColorType colorType) => Math.Max(1, colorType.GetBytesPerPixel());

    public static long Of(long width, long height, SKColorType colorType) => width * height * PerPixel(colorType);

    public static long Of(SKBitmap bitmap) => Of(bitmap.Width, bitmap.Height, bitmap.ColorType);

    public static long Of(SKImage? image) =>
        image is null || image.Handle == IntPtr.Zero ? 0 : Of(image.Width, image.Height, image.ColorType);
}
