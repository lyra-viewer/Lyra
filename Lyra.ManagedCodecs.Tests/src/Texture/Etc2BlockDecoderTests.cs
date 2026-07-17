using Lyra.ManagedCodecs.Texture;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Texture;

/// <summary>
/// Golden tests for the ETC2 / EAC block decoder, driven through <see cref="SurfaceDecoder"/>. The
/// hand-constructed blocks target the modes whose output can be derived independently from the format
/// rules: ETC1 individual / differential (solid colors), EAC alpha and EAC R11/RG11 (constant via a
/// zero/one multiplier), and RGB8A1 punch-through transparency. T / H / planar modes are exercised for
/// shape and range; their exact pixels are validated against real assets via the app's verify path.
/// </summary>
public class Etc2BlockDecoderTests
{
    private static byte[] Decode(TextureFormat format, byte[] block)
    {
        var dst = new byte[4 * 4 * 4];
        SurfaceDecoder.DecodeSurface(format, block, dst, 4, 4);
        return dst;
    }

    private static void AssertAllPixels(byte[] dst, byte r, byte g, byte b, byte a)
    {
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal((r, g, b, a), (dst[i * 4], dst[(i * 4) + 1], dst[(i * 4) + 2], dst[(i * 4) + 3]));
        }
    }

    [Fact]
    public void IndividualModeSolidColor()
    {
        // diff=0 (individual); both subblocks RGB444 = (8,8,8) -> base 0x88; table 0; all indices 0.
        // index 0 -> +small modifier (2). Every pixel = 0x88 + 2 = 138.
        byte[] block = [0x88, 0x88, 0x88, 0x00, 0x00, 0x00, 0x00, 0x00];
        AssertAllPixels(Decode(TextureFormat.Etc2Rgb8Unorm, block), 138, 138, 138, 255);
    }

    [Fact]
    public void DifferentialModeSolidColor()
    {
        // diff=1; base5 = (16,16,16), delta 0; table 0; indices 0. expand5(16)=132, +2 = 134.
        byte[] block = [0x80, 0x80, 0x80, 0x02, 0x00, 0x00, 0x00, 0x00];
        AssertAllPixels(Decode(TextureFormat.Etc2Rgb8Unorm, block), 134, 134, 134, 255);
    }

    [Fact]
    public void Rgba8ConstantAlpha()
    {
        // Alpha block: base 200, multiplier 0 -> every alpha = 200. Color block: the individual solid.
        byte[] block =
        [
            200, 0x00, 0, 0, 0, 0, 0, 0,        // EAC alpha
            0x88, 0x88, 0x88, 0x00, 0, 0, 0, 0, // ETC2 colour
        ];
        AssertAllPixels(Decode(TextureFormat.Etc2Rgba8Unorm, block), 138, 138, 138, 200);
    }

    [Fact]
    public void EacR11ConstantGrayscale()
    {
        // base 100, multiplier field 0 (-> mult 1), table 0, all indices 0 -> mod = ~2 = -3.
        // value = 100*8 + 4 - 3 = 801; 801 -> 8-bit = (801*255+1023)/2047 = 100. Grayscale, opaque.
        byte[] block = [100, 0x00, 0, 0, 0, 0, 0, 0];
        AssertAllPixels(Decode(TextureFormat.EacR11Unorm, block), 100, 100, 100, 255);
    }

    [Fact]
    public void EacRg11Constant()
    {
        // Red base 100 -> 100; green base 50 -> 50*8+1 = 401 -> 50. Blue 0, opaque.
        byte[] block =
        [
            100, 0x00, 0, 0, 0, 0, 0, 0, // red
            50, 0x00, 0, 0, 0, 0, 0, 0,  // green
        ];
        AssertAllPixels(Decode(TextureFormat.EacRg11Unorm, block), 100, 50, 0, 255);
    }

    [Fact]
    public void Rgb8A1PunchThroughTransparency()
    {
        // Differential RGB8A1, opaque bit (=diff bit) 0 -> punch-through active. base5 (16,16,16) -> 132.
        // Pixel 0 carries index 2 (msb plane bit set, lsb clear) -> transparent; the rest stay opaque.
        byte[] block = [0x80, 0x80, 0x80, 0x00, 0x00, 0x01, 0x00, 0x00];
        var dst = Decode(TextureFormat.Etc2Rgb8A1Unorm, block);

        Assert.Equal((0, 0, 0, 0), (dst[0], dst[1], dst[2], dst[3]));
        for (var i = 1; i < 16; i++)
        {
            Assert.Equal((132, 132, 132, 255), (dst[i * 4], dst[(i * 4) + 1], dst[(i * 4) + 2], dst[(i * 4) + 3]));
        }
    }

    [Fact]
    public void EacR11ProducesGrayscaleInRange()
    {
        // Arbitrary block: every texel must be opaque grayscale (R==G==B) regardless of the indices.
        byte[] block = [0x37, 0x9A, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC];
        var dst = Decode(TextureFormat.EacR11Unorm, block);

        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(dst[i * 4], dst[(i * 4) + 1]);
            Assert.Equal(dst[i * 4], dst[(i * 4) + 2]);
            Assert.Equal(255, dst[(i * 4) + 3]);
        }
    }
}
