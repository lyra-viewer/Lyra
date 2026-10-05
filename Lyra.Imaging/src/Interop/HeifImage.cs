using Lyra.Imaging.Decoding.Support;
using SkiaSharp;
using static Lyra.Imaging.Interop.HeifNative;

namespace Lyra.Imaging.Interop;

/// <summary>An image libheif decoded: interleaved RGBA, or a monochrome alpha plane.</summary>
internal sealed unsafe class HeifImage(IntPtr image) : IDisposable
{
    private IntPtr _image = image;

    /// <summary>The RGBA plane's width; a coded image can be wider than the picture it holds.</summary>
    public int Width => heif_image_get_width(_image, ChannelInterleaved);

    public int Height => heif_image_get_height(_image, ChannelInterleaved);

    /// <summary>
    /// The top-left <paramref name="width"/> x <paramref name="height"/> of the RGBA plane, as
    /// straight alpha. Coded images can be padded on the right and bottom: HEVC pads to whole blocks.
    /// </summary>
    public SKBitmap CopyRgba(int width, int height, SKColorSpace? colorSpace, CancellationToken ct)
    {
        var plane = Plane(ChannelInterleaved, width, height, bytesPerPixel: 4, out var stride);

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul, colorSpace));
        if (bitmap.GetPixels() == IntPtr.Zero)
        {
            bitmap.Dispose();
            throw new OutOfMemoryException($"Could not allocate a {width}x{height} image.");
        }

        try
        {
            PixelCopy.CopyRows(plane, stride, bitmap, ct);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        return bitmap;
    }
    
    public void CopyAlphaInto(SKBitmap bitmap)
    {
        var alpha = (byte*)Plane(ChannelY, bitmap.Width, bitmap.Height, bytesPerPixel: 1, out var alphaStride);

        var dst = (byte*)bitmap.GetPixels();

        for (var y = 0; y < bitmap.Height; y++)
        {
            var dstRow = dst + (nint)y * bitmap.RowBytes;
            var alphaRow = alpha + (nint)y * alphaStride;

            for (var x = 0; x < bitmap.Width; x++)
                dstRow[x * 4 + 3] = alphaRow[x];
        }
    }

    private IntPtr Plane(int channel, int width, int height, int bytesPerPixel, out int stride)
    {
        var planeWidth = heif_image_get_width(_image, channel);
        var planeHeight = heif_image_get_height(_image, channel);

        if (planeWidth < width || planeHeight < height)
            throw new HeifException($"The image is {planeWidth}x{planeHeight}, smaller than {width}x{height}");

        var plane = heif_image_get_plane_readonly(_image, channel, out stride);
        if (plane == IntPtr.Zero)
            throw new HeifException("The image decoded without pixels");

        if (stride < (long)width * bytesPerPixel)
            throw new HeifException($"The image's rows are {stride} bytes, too short for {width} pixels");

        return plane;
    }

    public void Dispose()
    {
        if (_image == IntPtr.Zero)
            return;

        heif_image_release(_image);
        _image = IntPtr.Zero;
    }
}
