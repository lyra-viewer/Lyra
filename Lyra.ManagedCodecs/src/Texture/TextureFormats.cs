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
        TextureFormat.R8Unorm             => Plain(1, srgb: false, alpha: false),
        TextureFormat.R8Snorm             => Plain(1, srgb: false, alpha: false, signed: true),
        TextureFormat.R16Unorm            => Plain(2, srgb: false, alpha: false),
        TextureFormat.R8Uint              => Plain(1, srgb: false, alpha: false),
        TextureFormat.R8Sint              => Plain(1, srgb: false, alpha: false, signed: true),
        TextureFormat.Rgba4Unorm          => Plain(2, srgb: false, alpha: true),
        TextureFormat.Rgb5A1Unorm         => Plain(2, srgb: false, alpha: true),
        TextureFormat.Rgb565Unorm         => Plain(2, srgb: false, alpha: false),
        TextureFormat.Rgb10A2Unorm        => Plain(4, srgb: false, alpha: true),
        TextureFormat.Rg8Unorm            => Plain(2, srgb: false, alpha: false),
        TextureFormat.Rgb8Unorm           => Plain(3, srgb: false, alpha: false),
        TextureFormat.Rgb8UnormSrgb       => Plain(3, srgb: true, alpha: false),
        TextureFormat.Rgba8Unorm          => Uncompressed(srgb: false),
        TextureFormat.Rgba8UnormSrgb      => Uncompressed(srgb: true),
        TextureFormat.Rgba8Snorm          => Uncompressed(srgb: false, signed: true),
        TextureFormat.Bgra8Unorm          => Uncompressed(srgb: false),
        TextureFormat.Bgra8UnormSrgb      => Uncompressed(srgb: true),
        TextureFormat.Rgba16Float         => Float(8),
        TextureFormat.Rgba32Float         => Float(16),
        TextureFormat.R16Float            => Float(2, alpha: false),
        TextureFormat.R32Float            => Float(4, alpha: false),
        TextureFormat.Rgb16Float          => Float(6, alpha: false),
        TextureFormat.B10G11R11UFloat     => Float(4, alpha: false),
        TextureFormat.Rgb9E5UFloat        => Float(4, alpha: false),
        TextureFormat.Bc1RgbaUnorm        => Block(8,  srgb: false, alpha: true),
        TextureFormat.Bc1RgbaUnormSrgb    => Block(8,  srgb: true, alpha: true),
        TextureFormat.Bc2Unorm            => Block(16, srgb: false, alpha: true),
        TextureFormat.Bc2UnormSrgb        => Block(16, srgb: true, alpha: true),
        TextureFormat.Bc3Unorm            => Block(16, srgb: false, alpha: true),
        TextureFormat.Bc3UnormSrgb        => Block(16, srgb: true, alpha: true),
        TextureFormat.Bc4Unorm            => Block(8,  srgb: false),
        TextureFormat.Bc4Snorm            => Block(8,  srgb: false, signed: true),
        TextureFormat.Bc5Unorm            => Block(16, srgb: false),
        TextureFormat.Bc5Snorm            => Block(16, srgb: false, signed: true),
        TextureFormat.Bc7Unorm            => Block(16, srgb: false, alpha: true),
        TextureFormat.Bc7UnormSrgb        => Block(16, srgb: true, alpha: true),
        TextureFormat.Bc6HUFloat          => Block(16, srgb: false, hdr: true),
        TextureFormat.Bc6HSFloat          => Block(16, srgb: false, hdr: true),
        TextureFormat.Etc2Rgb8Unorm       => Block(8,  srgb: false),
        TextureFormat.Etc2Rgb8UnormSrgb   => Block(8,  srgb: true),
        TextureFormat.Etc2Rgb8A1Unorm     => Block(8,  srgb: false, alpha: true),
        TextureFormat.Etc2Rgb8A1UnormSrgb => Block(8,  srgb: true, alpha: true),
        TextureFormat.Etc2Rgba8Unorm      => Block(16, srgb: false, alpha: true),
        TextureFormat.Etc2Rgba8UnormSrgb  => Block(16, srgb: true, alpha: true),
        TextureFormat.EacR11Unorm         => Block(8,  srgb: false),
        TextureFormat.EacR11Snorm         => Block(8,  srgb: false, signed: true),
        TextureFormat.EacRg11Unorm        => Block(16, srgb: false),
        TextureFormat.EacRg11Snorm        => Block(16, srgb: false, signed: true),
        TextureFormat.Astc4x4Unorm        => Astc(4, 4, srgb: false),
        TextureFormat.Astc4x4UnormSrgb    => Astc(4, 4, srgb: true),
        TextureFormat.Astc5x4Unorm        => Astc(5, 4, srgb: false),
        TextureFormat.Astc5x4UnormSrgb    => Astc(5, 4, srgb: true),
        TextureFormat.Astc5x5Unorm        => Astc(5, 5, srgb: false),
        TextureFormat.Astc5x5UnormSrgb    => Astc(5, 5, srgb: true),
        TextureFormat.Astc6x5Unorm        => Astc(6, 5, srgb: false),
        TextureFormat.Astc6x5UnormSrgb    => Astc(6, 5, srgb: true),
        TextureFormat.Astc6x6Unorm        => Astc(6, 6, srgb: false),
        TextureFormat.Astc6x6UnormSrgb    => Astc(6, 6, srgb: true),
        TextureFormat.Astc8x5Unorm        => Astc(8, 5, srgb: false),
        TextureFormat.Astc8x5UnormSrgb    => Astc(8, 5, srgb: true),
        TextureFormat.Astc8x6Unorm        => Astc(8, 6, srgb: false),
        TextureFormat.Astc8x6UnormSrgb    => Astc(8, 6, srgb: true),
        TextureFormat.Astc8x8Unorm        => Astc(8, 8, srgb: false),
        TextureFormat.Astc8x8UnormSrgb    => Astc(8, 8, srgb: true),
        TextureFormat.Astc10x5Unorm       => Astc(10, 5, srgb: false),
        TextureFormat.Astc10x5UnormSrgb   => Astc(10, 5, srgb: true),
        TextureFormat.Astc10x6Unorm       => Astc(10, 6, srgb: false),
        TextureFormat.Astc10x6UnormSrgb   => Astc(10, 6, srgb: true),
        TextureFormat.Astc10x8Unorm       => Astc(10, 8, srgb: false),
        TextureFormat.Astc10x8UnormSrgb   => Astc(10, 8, srgb: true),
        TextureFormat.Astc10x10Unorm      => Astc(10, 10, srgb: false),
        TextureFormat.Astc10x10UnormSrgb  => Astc(10, 10, srgb: true),
        TextureFormat.Astc12x10Unorm      => Astc(12, 10, srgb: false),
        TextureFormat.Astc12x10UnormSrgb  => Astc(12, 10, srgb: true),
        TextureFormat.Astc12x12Unorm      => Astc(12, 12, srgb: false),
        TextureFormat.Astc12x12UnormSrgb  => Astc(12, 12, srgb: true),
        TextureFormat.Astc3x3x3Unorm      => Astc3D(3, 3, 3, srgb: false),
        TextureFormat.Astc3x3x3UnormSrgb  => Astc3D(3, 3, 3, srgb: true),
        TextureFormat.Astc4x3x3Unorm      => Astc3D(4, 3, 3, srgb: false),
        TextureFormat.Astc4x3x3UnormSrgb  => Astc3D(4, 3, 3, srgb: true),
        TextureFormat.Astc4x4x3Unorm      => Astc3D(4, 4, 3, srgb: false),
        TextureFormat.Astc4x4x3UnormSrgb  => Astc3D(4, 4, 3, srgb: true),
        TextureFormat.Astc4x4x4Unorm      => Astc3D(4, 4, 4, srgb: false),
        TextureFormat.Astc4x4x4UnormSrgb  => Astc3D(4, 4, 4, srgb: true),
        TextureFormat.Astc5x4x4Unorm      => Astc3D(5, 4, 4, srgb: false),
        TextureFormat.Astc5x4x4UnormSrgb  => Astc3D(5, 4, 4, srgb: true),
        TextureFormat.Astc5x5x4Unorm      => Astc3D(5, 5, 4, srgb: false),
        TextureFormat.Astc5x5x4UnormSrgb  => Astc3D(5, 5, 4, srgb: true),
        TextureFormat.Astc5x5x5Unorm      => Astc3D(5, 5, 5, srgb: false),
        TextureFormat.Astc5x5x5UnormSrgb  => Astc3D(5, 5, 5, srgb: true),
        TextureFormat.Astc6x5x5Unorm      => Astc3D(6, 5, 5, srgb: false),
        TextureFormat.Astc6x5x5UnormSrgb  => Astc3D(6, 5, 5, srgb: true),
        TextureFormat.Astc6x6x5Unorm      => Astc3D(6, 6, 5, srgb: false),
        TextureFormat.Astc6x6x5UnormSrgb  => Astc3D(6, 6, 5, srgb: true),
        TextureFormat.Astc6x6x6Unorm      => Astc3D(6, 6, 6, srgb: false),
        TextureFormat.Astc6x6x6UnormSrgb  => Astc3D(6, 6, 6, srgb: true),
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

    /// <summary>An uncompressed/packed format with a 1x1 "block" of <paramref name="bytesPerPixel"/> bytes.</summary>
    private static TextureFormatInfo Plain(int bytesPerPixel, bool srgb, bool alpha, bool signed = false) => new()
    {
        IsCompressed = false,
        BlockWidth = 1,
        BlockHeight = 1,
        BytesPerBlock = bytesPerPixel,
        IsSrgb = srgb,
        IsSigned = signed,
        HasAlpha = alpha,
    };

    private static TextureFormatInfo Float(int bytesPerBlock, bool alpha = true) => new()
    {
        IsCompressed = false,
        BlockWidth = 1,
        BlockHeight = 1,
        BytesPerBlock = bytesPerBlock,
        IsHdr = true,
        HasAlpha = alpha,
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

    private static TextureFormatInfo Astc(int blockWidth, int blockHeight, bool srgb) => new()
    {
        IsCompressed = true,
        IsAstc = true,
        BlockWidth = blockWidth,
        BlockHeight = blockHeight,
        BytesPerBlock = 16, // every ASTC footprint is a 128-bit block
        IsSrgb = srgb,
        HasAlpha = true, // ASTC always carries (possibly opaque) alpha
    };

    private static TextureFormatInfo Astc3D(int blockWidth, int blockHeight, int blockDepth, bool srgb) => new()
    {
        IsCompressed = true,
        IsAstc = true,
        IsAstc3D = true,
        BlockWidth = blockWidth,
        BlockHeight = blockHeight,
        BlockDepth = blockDepth,
        BytesPerBlock = 16, // a 3D ASTC block is still 128 bits, just spanning Z
        IsSrgb = srgb,
        HasAlpha = true,
    };

    /// <summary>
    /// Bytes occupied by one volume surface of a 3D-block (ASTC 3D) format: whole blocks in all three
    /// dimensions. <c>checked</c> so a hostile header can't wrap. For 2D formats use <see cref="SurfaceByteSize"/>.
    /// </summary>
    public static long SurfaceByteSize3D(TextureFormat format, int width, int height, int depth)
    {
        if (width <= 0 || height <= 0 || depth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"Texture: invalid volume size {width}x{height}x{depth}.");
        }

        var info = Info(format);
        var blocksX = CeilDiv(width, info.BlockWidth);
        var blocksY = CeilDiv(height, info.BlockHeight);
        var blocksZ = CeilDiv(depth, info.BlockDepth <= 0 ? 1 : info.BlockDepth);

        return checked((long)blocksX * blocksY * blocksZ * info.BytesPerBlock);
    }
}