namespace Lyra.ManagedCodecs.Raster;

public readonly struct DecodedImage
{
    public DecodedImage(byte[] pixels, int width, int height)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
    }

    /// <summary>RGBA8888 pixel data. Length is always <c>Width * Height * 4</c>.</summary>
    public byte[] Pixels { get; }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; }
}