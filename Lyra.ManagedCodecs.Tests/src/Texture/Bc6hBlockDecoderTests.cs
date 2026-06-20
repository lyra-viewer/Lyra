using Lyra.ManagedCodecs.Texture;
using Lyra.ManagedCodecs.Texture.Dds;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Texture;

public class Bc6hBlockDecoderTests
{
    // A real unsigned BC6H block; expected texel values were captured from imagecodecs' independent
    // decoder. Note the HDR range (blue ~1200-1600) that BC6H exists to represent.
    private static readonly byte[] Block =
    [
        0x8B, 0x4A, 0xE5, 0xF1, 0xA9, 0x41, 0x06, 0xA0,
        0x95, 0x6A, 0x26, 0xAF, 0xBC, 0xCD, 0xAF, 0xE5,
    ];

    [Theory]
    [InlineData(0, 0, 0.000766754f, 0.005462646f, 1227.0f)]
    [InlineData(3, 3, 0.000922680f, 0.006637573f, 1622.0f)]
    [InlineData(1, 2, 0.000883102f, 0.006340027f, 1522.0f)]
    public void DecodesUnsignedBc6hAgainstReference(int x, int y, float er, float eg, float eb)
    {
        var dst = new float[4 * 4 * 4];
        SurfaceDecoder.DecodeSurfaceHdr(TextureFormat.Bc6HUFloat, Block, dst, 4, 4);

        var i = ((y * 4) + x) * 4;
        AssertClose(er, dst[i + 0]);
        AssertClose(eg, dst[i + 1]);
        AssertClose(eb, dst[i + 2]);
        Assert.Equal(1f, dst[i + 3]);
    }

    [Fact]
    public void DdsReaderMapsDx10Bc6h()
    {
        Assert.Equal(TextureFormat.Bc6HUFloat, DdsReader.Read(DdsTestFile.Dx10(4, 4, 1, 95, new byte[16])).Format);
        Assert.Equal(TextureFormat.Bc6HSFloat, DdsReader.Read(DdsTestFile.Dx10(4, 4, 1, 96, new byte[16])).Format);
        Assert.True(DdsReader.Read(DdsTestFile.Dx10(4, 4, 1, 95, new byte[16])).IsHdr);
    }

    private static void AssertClose(float expected, float actual)
    {
        var tol = Math.Max(1e-4f, Math.Abs(expected) * 1e-3f);
        Assert.True(Math.Abs(expected - actual) <= tol, $"expected {expected}, got {actual}");
    }
}