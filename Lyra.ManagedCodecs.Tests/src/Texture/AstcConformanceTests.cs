using Lyra.ManagedCodecs.Raster.Tga;
using Lyra.ManagedCodecs.Texture;
using Lyra.ManagedCodecs.Texture.Blocks.Astc;
using Lyra.ManagedCodecs.Texture.Ktx;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Texture;

/// <summary>
/// End-to-end ASTC conformance: decode the committed <c>.astc</c> fixtures with our managed decoder and
/// compare against astcenc's own reference decode (the <c>.ref.tga</c> files). Covers the block decoder
/// directly, the <see cref="SurfaceDecoder"/> tiling path, and the full <see cref="KtxReader"/> pipeline.
/// astcenc's LDR-to-8-bit uses an FP16 intermediate; we use the unorm8 decode path, so an exact-1
/// rounding difference is allowed (sRGB, which is pure unorm8, matches bit-exactly).
/// </summary>
public class AstcConformanceTests
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "src", "Texture", "Fixtures", "Astc");

    [Theory]
    [InlineData("ldr_4x4_24")]
    [InlineData("ldr_6x6_24")]
    [InlineData("ldr_8x8_24")]
    [InlineData("ldr_8x8_20")]
    [InlineData("srgb_4x4_24")]
    public void BlockDecoderMatchesAstcenc(string name)
    {
        var (blockW, blockH, width, height, blocks) = LoadAstc(name);

        var decoded = new byte[width * height * 4];
        var blocksX = (width + blockW - 1) / blockW;
        var blocksY = (height + blockH - 1) / blockH;
        Span<byte> blockRgba = stackalloc byte[12 * 12 * 4];

        var offset = 16; // skip the .astc header
        for (var by = 0; by < blocksY; by++)
        for (var bx = 0; bx < blocksX; bx++)
        {
            Assert.True(AstcBlockDecoder.TryDecode(blocks.AsSpan(offset, 16), blockW, blockH, blockRgba));
            offset += 16;
            CopyBlock(blockRgba, decoded, bx, by, blockW, blockH, width, height);
        }

        AssertMatchesReference(name, decoded);
    }

    [Fact]
    public void SurfaceDecoderTilesAstc()
    {
        // The 20x20 fixture exercises footprint tiling and edge clipping through the public surface API.
        var (_, _, width, height, blocks) = LoadAstc("ldr_8x8_20");
        var payload = blocks.AsSpan(16); // skip the .astc header; the rest is the raw block stream

        var decoded = new byte[width * height * 4];
        SurfaceDecoder.DecodeSurface(TextureFormat.Astc8x8Unorm, payload, decoded, width, height);

        AssertMatchesReference("ldr_8x8_20", decoded);
    }

    [Fact]
    public void DecodesThroughKtxPipeline()
    {
        // Wrap the fixture's block stream in a KTX1 (GL_COMPRESSED_RGBA_ASTC_8x8_KHR) and decode via the
        // real reader + TextureData.Decode path.
        var (_, _, width, height, blocks) = LoadAstc("ldr_8x8_20");
        var ktx = KtxTestFile.Ktx1(0x93B7, width, height, [blocks[16..]]);

        var texture = KtxReader.Read(ktx);
        Assert.Equal(TextureFormat.Astc8x8Unorm, texture.Format);

        var surface = texture.Subresources[0];
        var decoded = new byte[width * height * 4];
        texture.Decode(surface, decoded);

        AssertMatchesReference("ldr_8x8_20", decoded);
    }

    [Fact]
    public void Decodes3DBlockLayerMatchesAstcenc()
    {
        // 6x6x6 volume (24^3). The displayed slice is Z=0: the Z=0 plane (first bw*bh texels) of each
        // block in the leading Z-block layer. This drives the 3D block-mode, simplex infill, and 3D
        // partition paths directly.
        var (bw, bh, bd, width, height, _, blocks) = LoadAstc3D("astc3d_6x6x6_24");
        var blocksX = (width + bw - 1) / bw;
        var blocksY = (height + bh - 1) / bh;

        var decoded = new byte[width * height * 4];
        Span<byte> blockRgba = stackalloc byte[6 * 6 * 6 * 4];

        var offset = 16; // skip the .astc header
        for (var by = 0; by < blocksY; by++)
        for (var bx = 0; bx < blocksX; bx++)
        {
            Assert.True(AstcBlockDecoder.TryDecode(blocks.AsSpan(offset, 16), bw, bh, bd, blockRgba));
            offset += 16;
            CopyBlock(blockRgba, decoded, bx, by, bw, bh, width, height);
        }

        AssertMatchesReference("astc3d_6x6x6_24", decoded);
    }

    [Fact]
    public void Decodes3DVolumeThroughKtxPipeline()
    {
        // The full reader + surface path: a GL_..._ASTC_6x6x6_OES volume, decoded to its Z=0 slice.
        var (_, _, _, width, height, depth, blocks) = LoadAstc3D("astc3d_6x6x6_24");
        var ktx = KtxTestFile.Ktx1Volume(0x93C9, width, height, depth, blocks[16..]);

        var texture = KtxReader.Read(ktx);
        Assert.Equal(TextureFormat.Astc6x6x6Unorm, texture.Format);
        Assert.Equal(TextureKind.Volume, texture.Kind);
        Assert.Equal((24, 24, 24), (texture.Width, texture.Height, texture.Depth));

        var surface = texture.Subresources[0];
        var decoded = new byte[width * height * 4];
        texture.Decode(surface, decoded);

        AssertMatchesReference("astc3d_6x6x6_24", decoded);
    }
    
    private static (int BlockW, int BlockH, int BlockD, int Width, int Height, int Depth, byte[] Bytes) LoadAstc3D(string name)
    {
        var astc = File.ReadAllBytes(Path.Combine(FixtureDir, $"{name}.astc"));
        Assert.True(astc[0] == 0x13 && astc[1] == 0xAB && astc[2] == 0xA1 && astc[3] == 0x5C, "bad .astc magic");

        var width = astc[7] | (astc[8] << 8) | (astc[9] << 16);
        var height = astc[10] | (astc[11] << 8) | (astc[12] << 16);
        var depth = astc[13] | (astc[14] << 8) | (astc[15] << 16);
        return (astc[4], astc[5], astc[6], width, height, depth, astc);
    }

    private static (int BlockW, int BlockH, int Width, int Height, byte[] Bytes) LoadAstc(string name)
    {
        var astc = File.ReadAllBytes(Path.Combine(FixtureDir, $"{name}.astc"));
        Assert.True(astc[0] == 0x13 && astc[1] == 0xAB && astc[2] == 0xA1 && astc[3] == 0x5C, "bad .astc magic");

        var blockW = astc[4];
        var blockH = astc[5];
        var width = astc[7] | (astc[8] << 8) | (astc[9] << 16);
        var height = astc[10] | (astc[11] << 8) | (astc[12] << 16);
        return (blockW, blockH, width, height, astc);
    }

    private static void CopyBlock(ReadOnlySpan<byte> blockRgba, byte[] dst, int bx, int by, int blockW, int blockH, int width, int height)
    {
        var pxX = bx * blockW;
        var pxY = by * blockH;
        var copyW = Math.Min(blockW, width - pxX);
        var copyH = Math.Min(blockH, height - pxY);
        for (var ry = 0; ry < copyH; ry++)
        {
            var src = blockRgba.Slice(ry * blockW * 4, copyW * 4);
            src.CopyTo(dst.AsSpan((((pxY + ry) * width) + pxX) * 4, copyW * 4));
        }
    }

    private static void AssertMatchesReference(string name, byte[] decoded)
    {
        var reference = TgaReader.Decode(File.ReadAllBytes(Path.Combine(FixtureDir, $"{name}.ref.tga")));
        Assert.Equal(reference.Pixels.Length, decoded.Length);

        var maxDelta = 0;
        for (var i = 0; i < decoded.Length; i++)
        {
            maxDelta = Math.Max(maxDelta, Math.Abs(decoded[i] - reference.Pixels[i]));
        }

        Assert.True(maxDelta <= 1, $"{name}: max channel delta {maxDelta} vs astcenc reference");
    }
}
