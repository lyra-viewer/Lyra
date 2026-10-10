using Lyra.ManagedCodecs.Texture;
using Lyra.ManagedCodecs.Texture.Blocks.Astc;
using Lyra.ManagedCodecs.Texture.Ktx;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Texture;

/// <summary>
/// HDR ASTC against astcenc. <c>hdr_4x4_28x4.astc</c> is seven blocks astcenc encoded from synthetic
/// HDR content (grey and colored ramps up to ~20x white, an LDR alpha, an HDR alpha), between them
/// using endpoint modes 2, 7, 11, 14 and 15; <c>.ref.f32</c> is astcenc's HDR-profile decode of them,
/// float32 RGBA, which the decoder must match bit for bit.
/// </summary>
public class AstcHdrTests
{
    private const int Width = 28, Height = 4;

    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "src", "Texture", "Fixtures", "Astc");

    private static byte[] Blocks() => File.ReadAllBytes(Path.Combine(FixtureDir, "hdr_4x4_28x4.astc"))[16..];

    private static float[] Reference()
    {
        var bytes = File.ReadAllBytes(Path.Combine(FixtureDir, "hdr_4x4_28x4.ref.f32"));
        var floats = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    [Fact]
    public void TheFixtureCoversTheHdrEndpointModes()
    {
        var blocks = Blocks();
        var used = new HashSet<int>();
        Span<int> modes = stackalloc int[4];

        for (var offset = 0; offset < blocks.Length; offset += 16)
        {
            var partitions = AstcBlockDecoder.EndpointModes(blocks.AsSpan(offset, 16), is3d: false, modes);
            foreach (var mode in modes[..partitions])
                used.Add(mode);
        }

        Assert.Superset(new HashSet<int> { 2, 7, 11, 14, 15 }, used);
    }

    [Fact]
    public void HdrBlocksDecodeAsAstcencDoes()
    {
        var decoded = new float[Width * Height * 4];
        SurfaceDecoder.DecodeSurfaceHdr(TextureFormat.Astc4x4Unorm, Blocks(), decoded, Width, Height);

        AssertBitExact(Reference(), decoded);
    }

    [Fact]
    public void AnHdrSurfaceIsRecognisedByItsBlocks()
    {
        Assert.True(SurfaceDecoder.ContainsHdrBlocks(TextureFormat.Astc4x4Unorm, Blocks()));
        Assert.False(SurfaceDecoder.ContainsHdrBlocks(TextureFormat.Astc4x4UnormSrgb, Blocks()));
    }

    [Fact]
    public void AnLdrSurfaceIsNotHdr()
    {
        var ldr = File.ReadAllBytes(Path.Combine(FixtureDir, "ldr_4x4_24.astc"))[16..];

        Assert.False(SurfaceDecoder.ContainsHdrBlocks(TextureFormat.Astc4x4Unorm, ldr));
    }

    [Fact]
    public void TheEightBitDecodeRejectsAnHdrBlock()
    {
        Span<byte> rgba = stackalloc byte[4 * 4 * 4];

        Assert.False(AstcBlockDecoder.TryDecode(Blocks().AsSpan(0, 16), 4, 4, rgba));
    }

    [Fact]
    public void AKtx1OfHdrBlocksDecodesAsHdr()
    {
        // GL_COMPRESSED_RGBA_ASTC_4x4_KHR is the same token for LDR and HDR content.
        var texture = KtxReader.Read(KtxTestFile.Ktx1(0x93B0, Width, Height, [Blocks()]));

        Assert.True(texture.IsHdr);

        var decoded = new float[Width * Height * 4];
        texture.DecodeHdr(texture.Subresources[0], decoded);
        AssertBitExact(Reference(), decoded);
    }

    [Theory]
    [InlineData(1000066000u)] // VK_FORMAT_ASTC_4x4_SFLOAT_BLOCK
    [InlineData(157u)]        // VK_FORMAT_ASTC_4x4_UNORM_BLOCK, as most HDR KTX2 files have it
    public void AKtx2OfHdrBlocksDecodesAsHdr(uint vkFormat)
    {
        var texture = Ktx2Reader.Read(KtxTestFile.Ktx2(vkFormat, Width, Height, [Blocks()]));

        Assert.Equal(TextureFormat.Astc4x4Unorm, texture.Format);
        Assert.True(texture.IsHdr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AConstantBlockDecodesItsColor(bool hdr)
    {
        // Void extent: 0x1FC, the dynamic-range bit, two reserved ones, the all-ones (no) extent, then RGBA.
        ushort[] color = hdr
            ? [BitConverter.HalfToUInt16Bits((Half)12.5f), BitConverter.HalfToUInt16Bits((Half)0.25f), BitConverter.HalfToUInt16Bits((Half)3f), BitConverter.HalfToUInt16Bits((Half)1f)]
            : [0xFFFF, 0x8000, 0x0000, 0xFFFF];

        var block = new byte[16];
        BitConverter.TryWriteBytes(block, 0xFFFF_FFFF_FFFF_FDFCul | (hdr ? 0x200ul : 0ul));
        for (var c = 0; c < 4; c++)
            BitConverter.TryWriteBytes(block.AsSpan(8 + (c * 2)), color[c]);

        Assert.Equal(hdr, AstcBlockDecoder.IsHdrBlock(block, is3d: false));

        Span<float> rgba = stackalloc float[4 * 4 * 4];
        Assert.True(AstcBlockDecoder.TryDecodeHdr(block, 4, 4, 1, rgba));

        float[] expected = hdr ? [12.5f, 0.25f, 3f, 1f] : [1f, AstcHdr.Unorm16ToFloat(0x8000), 0f, 1f];
        Assert.Equal(expected, rgba[..4].ToArray());
        Assert.Equal(expected, rgba[^4..].ToArray());

        Span<byte> rgba8 = stackalloc byte[4 * 4 * 4];
        Assert.Equal(!hdr, AstcBlockDecoder.TryDecode(block, 4, 4, rgba8));
    }

    private static void AssertBitExact(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);

        for (var i = 0; i < expected.Length; i++)
        {
            if (BitConverter.SingleToInt32Bits(expected[i]) != BitConverter.SingleToInt32Bits(actual[i]))
                Assert.Fail($"texel {i / 4}, channel {i % 4}: astcenc has {expected[i]}, decoded {actual[i]}");
        }
    }
}