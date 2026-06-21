using Lyra.ManagedCodecs.Texture;
using Lyra.ManagedCodecs.Texture.Ktx;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Texture;

public class TextureFormatsTests
{
    [Theory]
    [InlineData(TextureFormat.Astc6x6x6Unorm, 24, 24, 24, 1024)]   // 4x4x4 blocks * 16
    [InlineData(TextureFormat.Astc6x6x6Unorm, 6, 6, 6, 16)]        // one block
    [InlineData(TextureFormat.Astc6x6x6Unorm, 1, 1, 1, 16)]        // rounds up to a full block
    [InlineData(TextureFormat.Astc4x4x4Unorm, 8, 8, 8, 128)]       // 2x2x2 blocks * 16
    [InlineData(TextureFormat.Astc3x3x3Unorm, 3, 3, 7, 48)]        // 1x1x3 blocks (Z rounds up)
    public void SurfaceByteSize3DRoundsToBlocks(TextureFormat format, int w, int h, int d, long expected)
    {
        Assert.Equal(expected, TextureFormats.SurfaceByteSize3D(format, w, h, d));
    }

    [Theory]
    [InlineData(0x93C0u, TextureFormat.Astc3x3x3Unorm, "GL_COMPRESSED_RGBA_ASTC_3x3x3_OES")]
    [InlineData(0x93C9u, TextureFormat.Astc6x6x6Unorm, "GL_COMPRESSED_RGBA_ASTC_6x6x6_OES")]
    [InlineData(0x93C3u, TextureFormat.Astc4x4x4Unorm, "GL_COMPRESSED_RGBA_ASTC_4x4x4_OES")]
    [InlineData(0x93E0u, TextureFormat.Astc3x3x3UnormSrgb, "GL_COMPRESSED_SRGB8_ALPHA8_ASTC_3x3x3_OES")]
    [InlineData(0x93E9u, TextureFormat.Astc6x6x6UnormSrgb, "GL_COMPRESSED_SRGB8_ALPHA8_ASTC_6x6x6_OES")]
    public void Maps3DAstcGlEnums(uint gl, TextureFormat expected, string name)
    {
        Assert.Equal(expected, KtxFormatMap.FromGl(gl));
        Assert.Equal(name, KtxFormatMap.GlName(gl));
    }

    [Theory]
    [InlineData(TextureFormat.Astc3x3x3Unorm, 3, 3, 3)]
    [InlineData(TextureFormat.Astc6x5x5Unorm, 6, 5, 5)]
    [InlineData(TextureFormat.Astc6x6x6UnormSrgb, 6, 6, 6)]
    public void Info3DReportsBlockGeometry(TextureFormat format, int bw, int bh, int bd)
    {
        var info = TextureFormats.Info(format);
        Assert.True(info.IsAstc);
        Assert.True(info.IsAstc3D);
        Assert.Equal((bw, bh, bd), (info.BlockWidth, info.BlockHeight, info.BlockDepth));
    }

    [Theory]
    [InlineData(TextureFormat.Bc1RgbaUnorm, 4, 4, 8)]    // one 4x4 block
    [InlineData(TextureFormat.Bc1RgbaUnorm, 1, 1, 8)]    // rounds up to a full block
    [InlineData(TextureFormat.Bc1RgbaUnorm, 5, 5, 32)]   // 2x2 blocks * 8
    [InlineData(TextureFormat.Bc1RgbaUnorm, 8, 8, 32)]   // 2x2 blocks * 8
    [InlineData(TextureFormat.Bc3Unorm, 4, 4, 16)]       // 16-byte block
    [InlineData(TextureFormat.Bc5Unorm, 16, 16, 256)]    // 4x4 blocks * 16
    [InlineData(TextureFormat.Rgba8Unorm, 2, 3, 24)]     // 6 px * 4
    [InlineData(TextureFormat.Bgra8Unorm, 7, 1, 28)]
    public void SurfaceByteSizeRoundsToBlocks(TextureFormat format, int w, int h, long expected)
    {
        Assert.Equal(expected, TextureFormats.SurfaceByteSize(format, w, h));
    }

    [Fact]
    public void SurfaceByteSizeStaysInLongRange()
    {
        // 65536^2 * 4 overflows int but is well within long.
        Assert.Equal(17_179_869_184L, TextureFormats.SurfaceByteSize(TextureFormat.Rgba8Unorm, 65536, 65536));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-1, 4)]
    public void SurfaceByteSizeRejectsNonPositive(int w, int h)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextureFormats.SurfaceByteSize(TextureFormat.Bc1RgbaUnorm, w, h));
    }

    [Theory]
    [InlineData(TextureFormat.Bc1RgbaUnorm, TextureFormat.Rgba8Unorm)]
    [InlineData(TextureFormat.Bc1RgbaUnormSrgb, TextureFormat.Rgba8UnormSrgb)]
    [InlineData(TextureFormat.Bc3UnormSrgb, TextureFormat.Rgba8UnormSrgb)]
    [InlineData(TextureFormat.Bc5Snorm, TextureFormat.Rgba8Unorm)]
    [InlineData(TextureFormat.Bgra8UnormSrgb, TextureFormat.Rgba8UnormSrgb)]
    public void DecodedFormatPreservesSrgb(TextureFormat format, TextureFormat expected)
    {
        Assert.Equal(expected, TextureFormats.DecodedFormat(format));
    }

    [Theory]
    [InlineData(TextureFormat.Bc1RgbaUnorm, true, 4, 4, 8)]
    [InlineData(TextureFormat.Bc3Unorm, true, 4, 4, 16)]
    [InlineData(TextureFormat.Rgba8Unorm, false, 1, 1, 4)]
    public void InfoReportsBlockGeometry(TextureFormat format, bool compressed, int bw, int bh, int bytes)
    {
        var info = TextureFormats.Info(format);
        Assert.Equal(compressed, info.IsCompressed);
        Assert.Equal(bw, info.BlockWidth);
        Assert.Equal(bh, info.BlockHeight);
        Assert.Equal(bytes, info.BytesPerBlock);
    }

    [Theory]
    [InlineData(TextureFormat.Bc1RgbaUnorm, true, 4)]   // 8 bytes / 16 px
    [InlineData(TextureFormat.Bc4Unorm, false, 4)]      // single-channel, no alpha
    [InlineData(TextureFormat.Bc7Unorm, true, 8)]       // 16 bytes / 16 px
    [InlineData(TextureFormat.Bc6HUFloat, false, 8)]    // HDR RGB, no alpha
    [InlineData(TextureFormat.Rgba8Unorm, true, 32)]
    [InlineData(TextureFormat.Rgba16Float, true, 64)]
    public void InfoReportsAlphaAndBitsPerPixel(TextureFormat format, bool hasAlpha, int bpp)
    {
        var info = TextureFormats.Info(format);
        Assert.Equal(hasAlpha, info.HasAlpha);
        Assert.Equal(bpp, info.BitsPerPixel);
    }
}
