using Lyra.ManagedCodecs.Texture;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Texture;

public class SurfaceDecoderTests
{
    private static (byte R, byte G, byte B, byte A) Px(byte[] rgba, int x, int y, int width)
    {
        var i = ((y * width) + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
    }

    [Fact]
    public void DecodesBc1FourColourBlock()
    {
        // color0 = red565 (0xF800) > color1 = blue565 (0x001F) -> opaque 4-color mode.
        // indices byte 0 = 0xE4 -> pixels 0,1,2,3 select entries 0,1,2,3; rest select 0.
        byte[] block = [0x00, 0xF8, 0x1F, 0x00, 0xE4, 0x00, 0x00, 0x00];

        var dst = new byte[4 * 4 * 4];
        SurfaceDecoder.DecodeSurface(TextureFormat.Bc1RgbaUnorm, block, dst, 4, 4);

        Assert.Equal((255, 0, 0, 255), Px(dst, 0, 0, 4));   // c0 red
        Assert.Equal((0, 0, 255, 255), Px(dst, 1, 0, 4));   // c1 blue
        Assert.Equal((170, 0, 85, 255), Px(dst, 2, 0, 4));  // c2 = (2c0+c1)/3
        Assert.Equal((85, 0, 170, 255), Px(dst, 3, 0, 4));  // c3 = (c0+2c1)/3
        Assert.Equal((255, 0, 0, 255), Px(dst, 0, 1, 4));   // index 0 -> red
    }

    [Fact]
    public void DecodesBc1PunchThroughAlpha()
    {
        // color0 = blue (0x001F) <= color1 = red (0xF800) -> 3-colour + transparent black at index 3.
        // indices byte 0 = 0xE4 -> pixel 3 selects index 3 (transparent).
        byte[] block = [0x1F, 0x00, 0x00, 0xF8, 0xE4, 0x00, 0x00, 0x00];

        var dst = new byte[4 * 4 * 4];
        SurfaceDecoder.DecodeSurface(TextureFormat.Bc1RgbaUnorm, block, dst, 4, 4);

        Assert.Equal((0, 0, 255, 255), Px(dst, 0, 0, 4));        // c0 blue
        Assert.Equal((255, 0, 0, 255), Px(dst, 1, 0, 4));        // c1 red
        Assert.Equal((0, 0, 0, 0), Px(dst, 3, 0, 4));            // index 3 -> transparent
    }

    [Fact]
    public void DecodesBc3InterpolatedAlpha()
    {
        // Alpha block: a0=255, a1=0 (a0>a1 -> 8-value palette). 3-bit indices: pixel i selects i (0..7),
        // remaining pixels select 0. Color block: solid white (c0=c1=0xFFFF).
        // alpha indices: 48 bits, 3 per pixel, pixels 0..7 = 0,1,2,3,4,5,6,7.
        ulong alphaIdx = 0;
        for (var i = 0; i < 8; i++) 
            alphaIdx |= (ulong)i << (3 * i);

        byte[] block = new byte[16];
        block[0] = 255; // a0
        block[1] = 0;   // a1
        for (var b = 0; b < 6; b++) 
            block[2 + b] = (byte)(alphaIdx >> (8 * b));
        
        block[8] = 0xFF; block[9] = 0xFF; // c0 = white
        block[10] = 0xFF; block[11] = 0xFF; // c1 = white

        var dst = new byte[4 * 4 * 4];
        SurfaceDecoder.DecodeSurface(TextureFormat.Bc3Unorm, block, dst, 4, 4);

        // a0=255 -> palette[0]=255, palette[1]=0, palette[2..7] interpolated 255..0.
        Assert.Equal(255, Px(dst, 0, 0, 4).A); // index 0
        Assert.Equal(0, Px(dst, 1, 0, 4).A);   // index 1
        Assert.Equal(219, Px(dst, 2, 0, 4).A);  // (6*255+1*0+3)/7 = 1533/7
        Assert.Equal(36, Px(dst, 3, 1, 4).A);   // pixel 7 (x3,y1), index 7 -> (1*255+6*0+3)/7
        Assert.Equal((255, 255, 255), (Px(dst, 0, 0, 4).R, Px(dst, 0, 0, 4).G, Px(dst, 0, 0, 4).B));
    }

    [Fact]
    public void DecodesBc4AsGrayscale()
    {
        // Single channel: r0=200, r1=40, all indices 0 -> every pixel = 200 in R=G=B.
        byte[] block = [200, 40, 0, 0, 0, 0, 0, 0];

        var dst = new byte[4 * 4 * 4];
        SurfaceDecoder.DecodeSurface(TextureFormat.Bc4Unorm, block, dst, 4, 4);

        Assert.Equal((200, 200, 200, 255), Px(dst, 0, 0, 4));
        Assert.Equal((200, 200, 200, 255), Px(dst, 3, 3, 4));
    }

    [Fact]
    public void HandlesPartialEdgeBlocks()
    {
        // 5x5 BC1 surface = 2x2 blocks. Solid red (c0=red, all indices 0). Every pixel must be red,
        // and nothing may be written outside the 5x5 destination.
        byte[] redBlock = [0x00, 0xF8, 0x00, 0xF8, 0x00, 0x00, 0x00, 0x00];
        var src = new byte[4 * 8];
        for (var b = 0; b < 4; b++) 
            redBlock.CopyTo(src, b * 8);

        var dst = new byte[5 * 5 * 4];
        SurfaceDecoder.DecodeSurface(TextureFormat.Bc1RgbaUnorm, src, dst, 5, 5);

        for (var y = 0; y < 5; y++)
            for (var x = 0; x < 5; x++)
                Assert.Equal((255, 0, 0, 255), Px(dst, x, y, 5));
    }

    [Fact]
    public void SwizzlesBgraToRgba()
    {
        // One 2x1 BGRA surface: pixel0 = B,G,R,A; pixel1.
        byte[] src = [10, 20, 30, 40, /**/ 50, 60, 70, 80];
        var dst = new byte[2 * 1 * 4];

        SurfaceDecoder.DecodeSurface(TextureFormat.Bgra8Unorm, src, dst, 2, 1);

        Assert.Equal((30, 20, 10, 40), Px(dst, 0, 0, 2));
        Assert.Equal((70, 60, 50, 80), Px(dst, 1, 0, 2));
    }

    [Fact]
    public void DecodesRgba16FloatToFloat32()
    {
        // One pixel: halves for 1.0 (0x3C00), 0.5 (0x3800), 2.0 (0x4000), 1.0 (0x3C00), little-endian.
        byte[] src = [0x00, 0x3C, 0x00, 0x38, 0x00, 0x40, 0x00, 0x3C];
        var dst = new float[1 * 1 * 4];

        SurfaceDecoder.DecodeSurfaceHdr(TextureFormat.Rgba16Float, src, dst, 1, 1);

        Assert.Equal(new[] { 1.0f, 0.5f, 2.0f, 1.0f }, dst);
    }

    [Fact]
    public void DecodesRgba8SnormWithRemap()
    {
        // Signed bytes: 127, 0, -128 (0x80 -> clamps to -127), -64 (0xC0). Remap [-1,1]->[0,255].
        byte[] src = [127, 0, 0x80, 0xC0];
        var dst = new byte[1 * 1 * 4];

        SurfaceDecoder.DecodeSurface(TextureFormat.Rgba8Snorm, src, dst, 1, 1);

        Assert.Equal(new byte[] { 255, 128, 0, 63 }, dst);
    }

    [Fact]
    public void RejectsShortSource()
    {
        var tooSmall = new byte[4]; // BC1 4x4 needs 8
        var dst = new byte[4 * 4 * 4];
        Assert.Throws<ArgumentException>(() => SurfaceDecoder.DecodeSurface(TextureFormat.Bc1RgbaUnorm, tooSmall, dst, 4, 4));
    }

    [Fact]
    public void DecodesR8AsGrayscale()
    {
        byte[] src = [100, 200];
        var dst = new byte[2 * 4];
        SurfaceDecoder.DecodeSurface(TextureFormat.R8Unorm, src, dst, 2, 1);

        Assert.Equal((100, 100, 100, 255), Px(dst, 0, 0, 2));
        Assert.Equal((200, 200, 200, 255), Px(dst, 1, 0, 2));
    }

    [Fact]
    public void DecodesR8UintAsGrayscale()
    {
        // R8_UINT integer values are shown directly: 100, 200 -> gray 100, 200.
        byte[] src = [100, 200];
        var dst = new byte[2 * 4];
        SurfaceDecoder.DecodeSurface(TextureFormat.R8Uint, src, dst, 2, 1);

        Assert.Equal((100, 100, 100, 255), Px(dst, 0, 0, 2));
        Assert.Equal((200, 200, 200, 255), Px(dst, 1, 0, 2));
    }

    [Fact]
    public void DecodesR8SintWithRemap()
    {
        // R8_SINT shares the snorm remap: 127, 0, -128 (clamps to -127), -64 -> 255, 128, 0, 63.
        byte[] src = [127, 0, 0x80, 0xC0];
        var dst = new byte[4 * 4];
        SurfaceDecoder.DecodeSurface(TextureFormat.R8Sint, src, dst, 4, 1);

        Assert.Equal((255, 255, 255, 255), Px(dst, 0, 0, 4));
        Assert.Equal((128, 128, 128, 255), Px(dst, 1, 0, 4));
        Assert.Equal((0, 0, 0, 255), Px(dst, 2, 0, 4));
        Assert.Equal((63, 63, 63, 255), Px(dst, 3, 0, 4));
    }

    [Fact]
    public void DecodesRg8AsRedGreen()
    {
        byte[] src = [50, 150];
        var dst = new byte[4];
        SurfaceDecoder.DecodeSurface(TextureFormat.Rg8Unorm, src, dst, 1, 1);
        Assert.Equal((50, 150, 0, 255), Px(dst, 0, 0, 1));
    }

    [Fact]
    public void DecodesRgb8WithOpaqueAlpha()
    {
        byte[] src = [10, 20, 30];
        var dst = new byte[4];
        SurfaceDecoder.DecodeSurface(TextureFormat.Rgb8Unorm, src, dst, 1, 1);
        Assert.Equal((10, 20, 30, 255), Px(dst, 0, 0, 1));
    }

    [Fact]
    public void DecodesRgb565()
    {
        // Pure red (0xF800) and pure green (0x07E0), little-endian.
        byte[] src = [0x00, 0xF8, 0xE0, 0x07];
        var dst = new byte[2 * 4];
        SurfaceDecoder.DecodeSurface(TextureFormat.Rgb565Unorm, src, dst, 2, 1);
        Assert.Equal((255, 0, 0, 255), Px(dst, 0, 0, 2));
        Assert.Equal((0, 255, 0, 255), Px(dst, 1, 0, 2));
    }

    [Fact]
    public void DecodesRgba4()
    {
        // R4G4B4A4 red, opaque = 0xF00F.
        byte[] src = [0x0F, 0xF0];
        var dst = new byte[4];
        SurfaceDecoder.DecodeSurface(TextureFormat.Rgba4Unorm, src, dst, 1, 1);
        Assert.Equal((255, 0, 0, 255), Px(dst, 0, 0, 1));
    }

    [Fact]
    public void DecodesRgb5A1()
    {
        // R5G5B5A1 red, alpha bit set = 0xF801.
        byte[] src = [0x01, 0xF8];
        var dst = new byte[4];
        SurfaceDecoder.DecodeSurface(TextureFormat.Rgb5A1Unorm, src, dst, 1, 1);
        Assert.Equal((255, 0, 0, 255), Px(dst, 0, 0, 1));
    }

    [Fact]
    public void DecodesRgb10A2()
    {
        // A2B10G10R10: red = 0x3FF, alpha = 3 -> 0xC00003FF.
        byte[] src = [0xFF, 0x03, 0x00, 0xC0];
        var dst = new byte[4];
        SurfaceDecoder.DecodeSurface(TextureFormat.Rgb10A2Unorm, src, dst, 1, 1);
        Assert.Equal((255, 0, 0, 255), Px(dst, 0, 0, 1));
    }

    [Fact]
    public void DecodesR16AsGrayscale()
    {
        byte[] src = [0x00, 0xFF]; // 0xFF00 -> high byte 0xFF
        var dst = new byte[4];
        SurfaceDecoder.DecodeSurface(TextureFormat.R16Unorm, src, dst, 1, 1);
        Assert.Equal((255, 255, 255, 255), Px(dst, 0, 0, 1));
    }

    [Fact]
    public void DecodesR32FloatAsGrayscale()
    {
        var src = BitConverter.GetBytes(2.5f);
        var dst = new float[4];
        SurfaceDecoder.DecodeSurfaceHdr(TextureFormat.R32Float, src, dst, 1, 1);
        Assert.Equal(new[] { 2.5f, 2.5f, 2.5f, 1f }, dst);
    }

    [Fact]
    public void DecodesRgb16Float()
    {
        var src = new byte[6];
        BitConverter.GetBytes((Half)1.0f).CopyTo(src, 0);
        BitConverter.GetBytes((Half)0.5f).CopyTo(src, 2);
        BitConverter.GetBytes((Half)2.0f).CopyTo(src, 4);

        var dst = new float[4];
        SurfaceDecoder.DecodeSurfaceHdr(TextureFormat.Rgb16Float, src, dst, 1, 1);
        Assert.Equal(new[] { 1f, 0.5f, 2f, 1f }, dst);
    }

    [Fact]
    public void DecodesB10G11R11UFloat()
    {
        // R=G=B=1.0 -> 11-bit float 0x3C0 (e=15,m=0), 10-bit float 0x1E0; packed R|G<<11|B<<22.
        byte[] src = [0xC0, 0x03, 0x1E, 0x78];
        var dst = new float[4];
        SurfaceDecoder.DecodeSurfaceHdr(TextureFormat.B10G11R11UFloat, src, dst, 1, 1);
        Assert.Equal(1f, dst[0], 3);
        Assert.Equal(1f, dst[1], 3);
        Assert.Equal(1f, dst[2], 3);
        Assert.Equal(1f, dst[3]);
    }

    [Fact]
    public void DecodesRgb9E5UFloat()
    {
        // R=G=B=1.0 via mantissa 256, shared exponent 16: 256 * 2^(16-24) = 1.0.
        byte[] src = [0x00, 0x01, 0x02, 0x84];
        var dst = new float[4];
        SurfaceDecoder.DecodeSurfaceHdr(TextureFormat.Rgb9E5UFloat, src, dst, 1, 1);
        Assert.Equal(1f, dst[0], 3);
        Assert.Equal(1f, dst[1], 3);
        Assert.Equal(1f, dst[2], 3);
        Assert.Equal(1f, dst[3]);
    }

    [Fact]
    public void DecodesAstcVoidExtentBlock()
    {
        // A void-extent ASTC block (low 9 bits = 0x1FC, LDR) with a constant 16-bit colour per channel;
        // the 8-bit result is the high byte. R=G=B=0xABAB->0xAB, A=0xFFFF->0xFF, across all 16 texels.
        byte[] block =
        [
            0xFC, 0x01, 0, 0, 0, 0, 0, 0,
            0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xFF, 0xFF,
        ];
        var dst = new byte[4 * 4 * 4];

        SurfaceDecoder.DecodeSurface(TextureFormat.Astc4x4Unorm, block, dst, 4, 4);

        for (var i = 0; i < 16; i++)
        {
            Assert.Equal((0xAB, 0xAB, 0xAB, 0xFF), (dst[i * 4], dst[(i * 4) + 1], dst[(i * 4) + 2], dst[(i * 4) + 3]));
        }
    }
}
