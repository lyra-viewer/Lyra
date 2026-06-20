namespace Lyra.ManagedCodecs.Texture;

/// <summary>
/// Format metadata and the surface-sizing math shared by container readers (to slice subresource
/// byte ranges) and the decoder (to bound its reads). <see cref="SurfaceByteSize"/> is the central
/// overflow choke point: it returns <see cref="long"/> and is <c>checked</c> so a hostile header can
/// never wrap into a small allocation or a too-short bounds check.
/// </summary>
public static class TextureFormats
{
    public static TextureFormatInfo Info(TextureFormat format) => format switch
    {
        TextureFormat.Rgba8Unorm       => Uncompressed(srgb: false),
        TextureFormat.Rgba8UnormSrgb   => Uncompressed(srgb: true),
        TextureFormat.Rgba8Snorm       => Uncompressed(srgb: false, signed: true),
        TextureFormat.Bgra8Unorm       => Uncompressed(srgb: false),
        TextureFormat.Bgra8UnormSrgb   => Uncompressed(srgb: true),
        TextureFormat.Rgba16Float      => Float(8),
        TextureFormat.Rgba32Float      => Float(16),
        TextureFormat.Bc1RgbaUnorm     => Block(8,  srgb: false, alpha: true),
        TextureFormat.Bc1RgbaUnormSrgb => Block(8,  srgb: true, alpha: true),
        TextureFormat.Bc2Unorm         => Block(16, srgb: false, alpha: true),
        TextureFormat.Bc2UnormSrgb     => Block(16, srgb: true, alpha: true),
        TextureFormat.Bc3Unorm         => Block(16, srgb: false, alpha: true),
        TextureFormat.Bc3UnormSrgb     => Block(16, srgb: true, alpha: true),
        TextureFormat.Bc4Unorm         => Block(8,  srgb: false),
        TextureFormat.Bc4Snorm         => Block(8,  srgb: false, signed: true),
        TextureFormat.Bc5Unorm         => Block(16, srgb: false),
        TextureFormat.Bc5Snorm         => Block(16, srgb: false, signed: true),
        TextureFormat.Bc7Unorm         => Block(16, srgb: false, alpha: true),
        TextureFormat.Bc7UnormSrgb     => Block(16, srgb: true, alpha: true),
        TextureFormat.Bc6HUFloat       => Block(16, srgb: false, hdr: true),
        TextureFormat.Bc6HSFloat       => Block(16, srgb: false, hdr: true),
        _ => throw new NotSupportedException($"Texture: no format info for {format}."),
    };

    /// <summary>
    /// Bytes occupied by one (single-slice) surface of the given pixel dimensions, with compressed
    /// formats rounded up to whole blocks. Throws <see cref="OverflowException"/> rather than wrap.
    /// </summary>
    public static long SurfaceByteSize(TextureFormat format, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"Texture: invalid surface size {width}x{height}.");
        }

        var info = Info(format);
        var blocksX = CeilDiv(width, info.BlockWidth);
        var blocksY = CeilDiv(height, info.BlockHeight);

        return checked((long)blocksX * blocksY * info.BytesPerBlock);
    }

    /// <summary>
    /// The uncompressed target a surface of <paramref name="format"/> decodes into, so callers can
    /// size their destination: HDR formats decode to RGBA float32, everything else to 8-bit RGBA with
    /// sRGB-ness preserved (BGRA is swizzled, snorm is remapped).
    /// </summary>
    public static TextureFormat DecodedFormat(TextureFormat format)
    {
        var info = Info(format);

        return info.IsHdr ? TextureFormat.Rgba32Float
            : info.IsSrgb ? TextureFormat.Rgba8UnormSrgb
            : TextureFormat.Rgba8Unorm;
    }

    private static int CeilDiv(int value, int divisor) => (value + divisor - 1) / divisor;

    private static TextureFormatInfo Uncompressed(bool srgb, bool signed = false) => new()
    {
        IsCompressed = false,
        BlockWidth = 1,
        BlockHeight = 1,
        BytesPerBlock = 4,
        IsSrgb = srgb,
        IsSigned = signed,
        HasAlpha = true, // the RGBA/BGRA 8-bit set all carry an alpha channel
    };

    private static TextureFormatInfo Float(int bytesPerBlock) => new()
    {
        IsCompressed = false,
        BlockWidth = 1,
        BlockHeight = 1,
        BytesPerBlock = bytesPerBlock,
        IsHdr = true,
        HasAlpha = true,
    };

    private static TextureFormatInfo Block(int bytesPerBlock, bool srgb, bool signed = false, bool hdr = false, bool alpha = false) => new()
    {
        IsCompressed = true,
        BlockWidth = 4,
        BlockHeight = 4,
        BytesPerBlock = bytesPerBlock,
        IsSrgb = srgb,
        IsSigned = signed,
        IsHdr = hdr,
        HasAlpha = alpha,
    };
}