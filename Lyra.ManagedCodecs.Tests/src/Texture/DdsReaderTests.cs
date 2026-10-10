using Lyra.ManagedCodecs.Texture;
using Lyra.ManagedCodecs.Texture.Dds;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Texture;

public class DdsReaderTests
{
    [Fact]
    public void ReadsLegacyDxt1()
    {
        var file = DdsTestFile.Legacy(4, 4, 1, "DXT1", DdsTestFile.RedBc1Block());
        var tex = DdsReader.Read(file);

        Assert.Equal(TextureFormat.Bc1RgbaUnorm, tex.Format);
        Assert.Equal(TextureKind.Texture2D, tex.Kind);
        Assert.Equal(4, tex.Width);
        Assert.Equal(4, tex.Height);
        Assert.Equal(1, tex.MipLevels);
        Assert.Single(tex.Subresources);

        var sr = tex.Subresources[0];
        Assert.Equal(0, sr.MipLevel);
        Assert.Equal(8, sr.Data.Length);
    }

    [Fact]
    public void ReadsLegacyFourCcAliases()
    {
        Assert.Equal(TextureFormat.Bc3Unorm, DdsReader.Read(DdsTestFile.Legacy(4, 4, 1, "DXT5", new byte[16])).Format);
        Assert.Equal(TextureFormat.Bc5Unorm, DdsReader.Read(DdsTestFile.Legacy(4, 4, 1, "ATI2", new byte[16])).Format);
        Assert.Equal(TextureFormat.Bc4Unorm, DdsReader.Read(DdsTestFile.Legacy(4, 4, 1, "BC4U", new byte[8])).Format);
    }

    [Fact]
    public void ReadsMipChainWithCorrectOffsets()
    {
        // 4x4 -> mips 4x4 (8) + 2x2 (8) + 1x1 (8) = 24 bytes of BC1.
        var data = new byte[24];
        for (var i = 0; i < 3; i++) 
            DdsTestFile.RedBc1Block().CopyTo(data, i * 8);
        
        var file = DdsTestFile.Legacy(4, 4, 3, "DXT1", data);
        var tex = DdsReader.Read(file);

        Assert.Equal(3, tex.MipLevels);
        Assert.Equal(3, tex.Subresources.Count);
        Assert.Equal((4, 4), (tex.Subresources[0].Width, tex.Subresources[0].Height));
        Assert.Equal((2, 2), (tex.Subresources[1].Width, tex.Subresources[1].Height));
        Assert.Equal((1, 1), (tex.Subresources[2].Width, tex.Subresources[2].Height));
    }

    [Fact]
    public void ReadsDx10Bc1()
    {
        var file = DdsTestFile.Dx10(4, 4, 1, dxgiFormat: 71, DdsTestFile.RedBc1Block());
        var tex = DdsReader.Read(file);

        Assert.Equal(TextureFormat.Bc1RgbaUnorm, tex.Format);
        Assert.Equal(TextureKind.Texture2D, tex.Kind);
    }

    [Fact]
    public void ReadsDx10Cubemap()
    {
        // 6 faces of a 4x4 BC1 block.
        var data = new byte[6 * 8];
        for (var f = 0; f < 6; f++) 
            DdsTestFile.RedBc1Block().CopyTo(data, f * 8);
        
        var file = DdsTestFile.Dx10(4, 4, 1, dxgiFormat: 71, data, miscFlag: 0x4);
        var tex = DdsReader.Read(file);

        Assert.Equal(TextureKind.Cube, tex.Kind);
        Assert.Equal(6, tex.Subresources.Count);
        Assert.Equal([0, 1, 2, 3, 4, 5], tex.Subresources.Select(s => s.Face).ToArray());
    }

    [Fact]
    public void DecodesBaseSurfaceThroughTextureData()
    {
        var tex = DdsReader.Read(DdsTestFile.Legacy(4, 4, 1, "DXT1", DdsTestFile.RedBc1Block()));
        var sr = tex.Subresources[0];

        var dst = Rgba8.BufferFor(sr);
        tex.Decode(sr, dst);

        Assert.Equal(4 * 4 * 4, dst.Length);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, dst[..4]); // top-left red
    }

    [Fact]
    public void ReadsNumericFourCcRgba16Float()
    {
        // 'q' = 113 = D3DFMT_A16B16G16R16F stored as a numeric FourCC. 4x4 * 8 bytes.
        var file = DdsTestFile.LegacyNumericFourCc(4, 4, 1, 113, new byte[4 * 4 * 8]);

        var tex = DdsReader.Read(file);
        Assert.Equal(TextureFormat.Rgba16Float, tex.Format);
        Assert.True(tex.IsHdr);
    }

    [Fact]
    public void ReadsBumpDudvAsSnorm()
    {
        // DDPF_BUMPDUDV (0x80000), 32bpp, RGBA-order masks -> signed RGBA8.
        var file = DdsTestFile.Masked(4, 4, 0x80000, 32, 0x000000FF, 0x0000FF00, 0x00FF0000, 0xFF000000, new byte[4 * 4 * 4]);

        Assert.Equal(TextureFormat.Rgba8Snorm, DdsReader.Read(file).Format);
    }

    [Fact]
    public void ReadsDx10Rgba16Float()
    {
        Assert.Equal(TextureFormat.Rgba16Float, DdsReader.Read(DdsTestFile.Dx10(4, 4, 1, 10, new byte[4 * 4 * 8])).Format);
    }

    [Fact]
    public void ExposesFormatName()
    {
        // Legacy ASCII FourCC keeps its literal spelling...
        Assert.Equal("DXT5", DdsReader.Read(DdsTestFile.Legacy(4, 4, 1, "DXT5", new byte[16])).FormatName);
        // ...DX10 gets the canonical DXGI-style name.
        Assert.Equal("BC7_UNORM", DdsReader.Read(DdsTestFile.Dx10(4, 4, 1, 98, new byte[16])).FormatName);
        // ...a numeric D3DFORMAT keeps the canonical name and notes the raw FourCC char (113 = 'q').
        Assert.Equal("R16G16B16A16_FLOAT (FourCC 'q')", DdsReader.Read(DdsTestFile.LegacyNumericFourCc(4, 4, 1, 113, new byte[4 * 4 * 8])).FormatName);
    }

    [Fact]
    public void ReadsDxgiAstc()
    {
        // DXGI_FORMAT 150 = ASTC_6X6_UNORM; a 6x6 surface is a single 128-bit block.
        var tex = DdsReader.Read(DdsTestFile.Dx10(6, 6, 1, dxgiFormat: 150, new byte[16]));
        Assert.Equal(TextureFormat.Astc6x6Unorm, tex.Format);
    }

    [Theory]
    [InlineData(16u, TextureFormat.Rg32Float)]
    [InlineData(34u, TextureFormat.Rg16Float)]
    [InlineData(41u, TextureFormat.R32Float)]
    [InlineData(54u, TextureFormat.R16Float)]
    [InlineData(26u, TextureFormat.B10G11R11UFloat)]
    [InlineData(67u, TextureFormat.Rgb9E5UFloat)]
    [InlineData(24u, TextureFormat.Rgb10A2Unorm)]
    [InlineData(56u, TextureFormat.R16Unorm)]
    [InlineData(62u, TextureFormat.R8Uint)]
    [InlineData(63u, TextureFormat.R8Snorm)]
    [InlineData(64u, TextureFormat.R8Sint)]
    [InlineData(65u, TextureFormat.A8Unorm)]
    [InlineData(85u, TextureFormat.Rgb565Unorm)]
    [InlineData(86u, TextureFormat.Bgr5A1Unorm)]
    [InlineData(88u, TextureFormat.Bgrx8Unorm)]
    [InlineData(93u, TextureFormat.Bgrx8UnormSrgb)]
    [InlineData(115u, TextureFormat.Bgra4Unorm)]
    public void ReadsDx10UncompressedFormats(uint dxgi, TextureFormat expected)
    {
        var data = new byte[TextureFormats.SurfaceByteSize(expected, 4, 4)];

        Assert.Equal(expected, DdsReader.Read(DdsTestFile.Dx10(4, 4, 1, dxgi, data)).Format);
    }

    // DDPF_ALPHAPIXELS 0x1, DDPF_ALPHA 0x2, DDPF_RGB 0x40, DDPF_LUMINANCE 0x20000.
    [Theory]
    [InlineData(0x41u, 32u, 0x00FF0000u, 0x0000FF00u, 0x000000FFu, 0xFF000000u, TextureFormat.Bgra8Unorm)]
    [InlineData(0x40u, 32u, 0x00FF0000u, 0x0000FF00u, 0x000000FFu, 0u, TextureFormat.Bgrx8Unorm)]
    [InlineData(0x40u, 32u, 0x000000FFu, 0x0000FF00u, 0x00FF0000u, 0u, TextureFormat.Rgbx8Unorm)]
    [InlineData(0x41u, 32u, 0x000003FFu, 0x000FFC00u, 0x3FF00000u, 0xC0000000u, TextureFormat.Rgb10A2Unorm)]
    [InlineData(0x40u, 24u, 0x00FF0000u, 0x0000FF00u, 0x000000FFu, 0u, TextureFormat.Bgr8Unorm)]
    [InlineData(0x40u, 24u, 0x000000FFu, 0x0000FF00u, 0x00FF0000u, 0u, TextureFormat.Rgb8Unorm)]
    [InlineData(0x40u, 16u, 0xF800u, 0x07E0u, 0x001Fu, 0u, TextureFormat.Rgb565Unorm)]
    [InlineData(0x41u, 16u, 0x7C00u, 0x03E0u, 0x001Fu, 0x8000u, TextureFormat.Bgr5A1Unorm)]
    [InlineData(0x40u, 16u, 0x7C00u, 0x03E0u, 0x001Fu, 0x8000u, TextureFormat.Bgr5X1Unorm)] // alpha mask without the flag
    [InlineData(0x41u, 16u, 0x0F00u, 0x00F0u, 0x000Fu, 0xF000u, TextureFormat.Bgra4Unorm)]
    [InlineData(0x20000u, 16u, 0xFFFFu, 0u, 0u, 0u, TextureFormat.R16Unorm)]
    [InlineData(0x20001u, 16u, 0x00FFu, 0u, 0u, 0xFF00u, TextureFormat.La8Unorm)]
    [InlineData(0x20000u, 8u, 0xFFu, 0u, 0u, 0u, TextureFormat.R8Unorm)]
    [InlineData(0x40u, 8u, 0xFFu, 0u, 0u, 0u, TextureFormat.R8Unorm)] // L8 as some writers flag it
    [InlineData(0x2u, 8u, 0u, 0u, 0u, 0xFFu, TextureFormat.A8Unorm)]
    public void ReadsLegacyMaskedFormats(uint flags, uint bits, uint r, uint g, uint b, uint a, TextureFormat expected)
    {
        var data = new byte[TextureFormats.SurfaceByteSize(expected, 4, 4)];

        Assert.Equal(expected, DdsReader.Read(DdsTestFile.Masked(4, 4, flags, bits, r, g, b, a, data)).Format);
    }

    [Theory]
    [InlineData(111u, TextureFormat.R16Float)]
    [InlineData(112u, TextureFormat.Rg16Float)]
    [InlineData(114u, TextureFormat.R32Float)]
    [InlineData(115u, TextureFormat.Rg32Float)]
    public void ReadsNumericFourCcFloatFormats(uint d3dFormat, TextureFormat expected)
    {
        var data = new byte[TextureFormats.SurfaceByteSize(expected, 4, 4)];

        Assert.Equal(expected, DdsReader.Read(DdsTestFile.LegacyNumericFourCc(4, 4, 1, d3dFormat, data)).Format);
    }

    [Theory]
    [InlineData("ETC ", TextureFormat.Etc2Rgb8Unorm)]
    [InlineData("ETC1", TextureFormat.Etc2Rgb8Unorm)]
    [InlineData("ETC2", TextureFormat.Etc2Rgb8Unorm)]
    [InlineData("ETCA", TextureFormat.Etc2Rgba8Unorm)]
    [InlineData("ETCP", TextureFormat.Etc2Rgb8A1Unorm)]
    public void ReadsToolSpecificEtcFourCcs(string fourCc, TextureFormat expected)
    {
        var data = new byte[TextureFormats.SurfaceByteSize(expected, 4, 4)];

        var texture = DdsReader.Read(DdsTestFile.Legacy(4, 4, 1, fourCc, data));

        Assert.Equal(expected, texture.Format);
        Assert.Equal(fourCc.TrimEnd(), texture.FormatName);
    }

    [Fact]
    public void RejectsBadMagic()
    {
        var file = DdsTestFile.Legacy(4, 4, 1, "DXT1", DdsTestFile.RedBc1Block());
        file[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => DdsReader.Read(file));
    }

    [Fact]
    public void RejectsTruncatedSurface()
    {
        // Header claims 4x4 BC1 (needs 8 bytes) but only 4 bytes of payload are present.
        var file = DdsTestFile.Legacy(4, 4, 1, "DXT1", new byte[4]);
        Assert.Throws<InvalidDataException>(() => DdsReader.Read(file));
    }

    [Fact]
    public void RejectsImplausibleMipCount()
    {
        // 4x4 allows at most 3 mips; claim 99.
        var file = DdsTestFile.Legacy(4, 4, 99, "DXT1", DdsTestFile.RedBc1Block());
        Assert.Throws<InvalidDataException>(() => DdsReader.Read(file));
    }

    [Fact]
    public void RejectsUnknownFormat()
    {
        var file = DdsTestFile.Legacy(4, 4, 1, "ZZZZ", new byte[16]);
        Assert.Throws<NotSupportedException>(() => DdsReader.Read(file));
    }

    [Fact]
    public void RejectsTooSmallFile()
    {
        Assert.Throws<InvalidDataException>(() => DdsReader.Read(new byte[64]));
    }
}
