namespace Lyra.ManagedCodecs.Raster;

/// <summary>
/// A decoded high-dynamic-range image: linear, scene-referred RGBA float pixels with
/// top-left origin. Unlike <see cref="DecodedImage"/> (8-bit, display-ready), these values
/// are not tone-mapped - the consumer is expected to apply its own tone curve.
/// </summary>
public readonly struct DecodedFloatImage
{
    public DecodedFloatImage(float[] pixels, int width, int height)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
    }

    /// <summary>RGBA float pixel data. Length is always <c>Width * Height * 4</c>.</summary>
    public float[] Pixels { get; }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; }
}
