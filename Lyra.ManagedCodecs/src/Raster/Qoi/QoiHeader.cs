namespace Lyra.ManagedCodecs.Raster.Qoi;

/// <summary>How a QOI file's channels are to be read. Informative only: decoding is the same.</summary>
public enum QoiColorSpace : byte
{
    /// <summary>sRGB color channels with a linear alpha channel.</summary>
    SrgbLinearAlpha = 0,

    /// <summary>Every channel linear.</summary>
    Linear = 1
}

/// <summary>
/// The fixed 14-byte QOI header. Width and height are big-endian.
/// <see href="https://qoiformat.org/qoi-specification.pdf"/>
/// </summary>
/// <param name="Channels">3 for RGB, 4 for RGBA. Informative only: the stream always decodes to RGBA.</param>
public readonly record struct QoiHeader(uint Width, uint Height, byte Channels, QoiColorSpace ColorSpace)
{
    public const int Size = 14;

    public long PixelCount => (long)Width * Height;
}
