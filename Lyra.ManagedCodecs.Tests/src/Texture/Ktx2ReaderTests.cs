using System.IO.Compression;
using Lyra.ManagedCodecs.Texture;
using Lyra.ManagedCodecs.Texture.Ktx;
using Xunit;
using ZstdSharp;

namespace Lyra.ManagedCodecs.Tests.Texture;

public class Ktx2ReaderTests
{
    private const uint VkRgba8Unorm = 37;
    private const uint VkR8Uint = 13;
    private const uint VkR8Sint = 14;
    private const uint VkRgba8Srgb = 43;
    private const uint VkBc1RgbaUnorm = 133;
    private const uint VkBc7Srgb = 146;
    private const uint VkEtc2Rgb8 = 147;
    private const uint VkAstc8x8Unorm = 171;
    private const uint VkAstc4x4SFloat = 1000066000; // HDR ASTC - not supported yet
    private const uint VkUndefined = 0;              // Basis Universal

    private const uint SchemeZstd = 2;
    private const uint SchemeZlib = 3;

    [Fact]
    public void ReadsRgba8Surface()
    {
        var file = KtxTestFile.Ktx2(VkRgba8Unorm, 4, 4, [KtxTestFile.Rgba8(4, 4, 10, 20, 30, 40)]);
        var tex = Ktx2Reader.Read(file);

        Assert.Equal(TextureFormat.Rgba8Unorm, tex.Format);
        Assert.Equal(TextureKind.Texture2D, tex.Kind);
        Assert.Equal(4, tex.Width);
        Assert.Equal(4, tex.Height);
        Assert.Single(tex.Subresources);
        Assert.Equal(4 * 4 * 4, tex.Subresources[0].Data.Length);
    }

    [Fact]
    public void ReadsR8IntegerSurfaces()
    {
        var uintTex = Ktx2Reader.Read(KtxTestFile.Ktx2(VkR8Uint, 2, 1, [[100, 200]]));
        Assert.Equal(TextureFormat.R8Uint, uintTex.Format);
        Assert.Equal("VK_FORMAT_R8_UINT", uintTex.FormatName);
        var uintDst = new byte[TextureData.DecodedByteSize(uintTex.Subresources[0])];
        uintTex.Decode(uintTex.Subresources[0], uintDst);
        Assert.Equal(new byte[] { 100, 100, 100, 255 }, uintDst[..4]);

        var sintTex = Ktx2Reader.Read(KtxTestFile.Ktx2(VkR8Sint, 2, 1, [[127, 0x80]]));
        Assert.Equal(TextureFormat.R8Sint, sintTex.Format);
        Assert.Equal("VK_FORMAT_R8_SINT", sintTex.FormatName);
    }

    [Fact]
    public void ReadsBc1Surface()
    {
        var tex = Ktx2Reader.Read(KtxTestFile.Ktx2(VkBc1RgbaUnorm, 4, 4, [KtxTestFile.RedBc1Block()]));
        Assert.Equal(TextureFormat.Bc1RgbaUnorm, tex.Format);
        Assert.Equal(8, tex.Subresources[0].Data.Length);
    }

    [Fact]
    public void ReadsMipChainWithCorrectDimensions()
    {
        var mips = new[] { KtxTestFile.RedBc1Block(), KtxTestFile.RedBc1Block(), KtxTestFile.RedBc1Block() };
        var tex = Ktx2Reader.Read(KtxTestFile.Ktx2(VkBc1RgbaUnorm, 4, 4, mips));

        Assert.Equal(3, tex.MipLevels);
        Assert.Equal((4, 4), (tex.Subresources[0].Width, tex.Subresources[0].Height));
        Assert.Equal((2, 2), (tex.Subresources[1].Width, tex.Subresources[1].Height));
        Assert.Equal((1, 1), (tex.Subresources[2].Width, tex.Subresources[2].Height));
    }

    [Fact]
    public void DecodesBaseSurfaceThroughTextureData()
    {
        var tex = Ktx2Reader.Read(KtxTestFile.Ktx2(VkBc1RgbaUnorm, 4, 4, [KtxTestFile.RedBc1Block()]));
        var dst = new byte[TextureData.DecodedByteSize(tex.Subresources[0])];
        tex.Decode(tex.Subresources[0], dst);

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, dst[..4]);
    }

    [Fact]
    public void ExposesVkFormatName()
    {
        var tex = Ktx2Reader.Read(KtxTestFile.Ktx2(VkBc7Srgb, 4, 4, [new byte[16]]));
        Assert.Equal(TextureFormat.Bc7UnormSrgb, tex.Format);
        Assert.Equal("VK_FORMAT_BC7_SRGB_BLOCK", tex.FormatName);
    }

    [Fact]
    public void DefaultsToTopLeftOrigin()
    {
        var tex = Ktx2Reader.Read(KtxTestFile.Ktx2(VkRgba8Unorm, 4, 4, [KtxTestFile.Rgba8(4, 4, 1, 2, 3, 4)]));
        Assert.Equal(TextureOrigin.TopLeft, tex.Origin);
    }

    [Fact]
    public void HonorsBottomUpOrientationMetadata()
    {
        var tex = Ktx2Reader.Read(KtxTestFile.Ktx2(VkRgba8Unorm, 4, 4, [KtxTestFile.Rgba8(4, 4, 1, 2, 3, 4)], orientation: "ru"));
        Assert.Equal(TextureOrigin.BottomLeft, tex.Origin);
    }

    [Fact]
    public void ReadsAndDecodesEtc2()
    {
        byte[] block = [0x88, 0x88, 0x88, 0x00, 0x00, 0x00, 0x00, 0x00];
        var tex = Ktx2Reader.Read(KtxTestFile.Ktx2(VkEtc2Rgb8, 4, 4, [block]));

        Assert.Equal(TextureFormat.Etc2Rgb8Unorm, tex.Format);

        var dst = new byte[TextureData.DecodedByteSize(tex.Subresources[0])];
        tex.Decode(tex.Subresources[0], dst);
        Assert.Equal(new byte[] { 138, 138, 138, 255 }, dst[..4]);
    }

    [Fact]
    public void ReadsZstdSupercompressedSurface()
    {
        var surface = KtxTestFile.Rgba8(4, 4, 10, 20, 30, 40);
        using var compressor = new Compressor();
        var compressed = compressor.Wrap(surface).ToArray();

        var file = KtxTestFile.Ktx2Supercompressed(VkRgba8Unorm, 4, 4, SchemeZstd, compressed, surface.Length);
        var tex = Ktx2Reader.Read(file);

        var dst = new byte[TextureData.DecodedByteSize(tex.Subresources[0])];
        tex.Decode(tex.Subresources[0], dst);
        Assert.Equal(surface, dst); // RGBA8 decodes to itself
    }

    [Fact]
    public void ReadsZlibSupercompressedSurface()
    {
        var surface = KtxTestFile.Rgba8(4, 4, 11, 22, 33, 44);
        var compressed = ZlibCompress(surface);

        var file = KtxTestFile.Ktx2Supercompressed(VkRgba8Unorm, 4, 4, SchemeZlib, compressed, surface.Length);
        var tex = Ktx2Reader.Read(file);

        var dst = new byte[TextureData.DecodedByteSize(tex.Subresources[0])];
        tex.Decode(tex.Subresources[0], dst);
        Assert.Equal(surface, dst);
    }

    [Fact]
    public void RejectsBasisLzSupercompression()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => Ktx2Reader.Read(KtxTestFile.Ktx2Supercompressed(VkUndefined, 4, 4, 1, new byte[8], 16)));
        Assert.Contains("BasisLZ", ex.Message);
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }

    [Fact]
    public void ReadsAstcStructure()
    {
        var tex = Ktx2Reader.Read(KtxTestFile.Ktx2(VkAstc8x8Unorm, 8, 8, [new byte[16]]));

        Assert.Equal(TextureFormat.Astc8x8Unorm, tex.Format);
        Assert.Equal("VK_FORMAT_ASTC_8x8_UNORM_BLOCK", tex.FormatName);
        Assert.Equal(16, tex.Subresources[0].Data.Length);
    }

    [Fact]
    public void RejectsHdrAstcAsUnsupported()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => Ktx2Reader.Read(KtxTestFile.Ktx2(VkAstc4x4SFloat, 4, 4, [new byte[16]])));
        Assert.Contains("ASTC HDR", ex.Message);
    }

    [Fact]
    public void RejectsBasisAsUnsupported()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => Ktx2Reader.Read(KtxTestFile.Ktx2(VkUndefined, 4, 4, [new byte[16]])));
        Assert.Contains("Basis", ex.Message);
    }

    [Fact]
    public void RejectsBadMagic()
    {
        var file = KtxTestFile.Ktx2(VkRgba8Unorm, 4, 4, [KtxTestFile.Rgba8(4, 4, 0, 0, 0, 0)]);
        file[1] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => Ktx2Reader.Read(file));
    }

    [Fact]
    public void RejectsTruncatedLevelData()
    {
        // Level index claims a 4x4 RGBA8 level (64 bytes); lop the payload off.
        var file = KtxTestFile.Ktx2(VkRgba8Unorm, 4, 4, [KtxTestFile.Rgba8(4, 4, 0, 0, 0, 0)]);
        Assert.Throws<InvalidDataException>(() => Ktx2Reader.Read(file[..^32]));
    }

    [Fact]
    public void RejectsTooSmallFile()
    {
        Assert.Throws<InvalidDataException>(() => Ktx2Reader.Read(new byte[40]));
    }
}