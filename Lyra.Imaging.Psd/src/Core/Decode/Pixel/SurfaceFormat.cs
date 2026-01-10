namespace Lyra.Imaging.Psd.Core.Decode.Pixel;

public readonly record struct SurfaceFormat(PixelFormat PixelFormat, AlphaType AlphaType)
{
    public static readonly SurfaceFormat Default = new(PixelFormat.Bgra8888, AlphaType.Premultiplied);
}

public enum PixelFormat
{
    Bgra8888,
    Rgba8888,
}

public enum AlphaType
{
    Straight,
    Premultiplied
}