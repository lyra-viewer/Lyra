using Lyra.ManagedCodecs.Texture;
using Lyra.ManagedCodecs.Texture.Ktx;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Texture;

public class KtxReaderTests
{
    private const uint GlRgba8 = 0x8058;
    private const uint GlCompressedRgbaDxt1 = 0x83F1;
    private const uint GlCompressedRgbaBptcUnorm = 0x8E8C; // BC7
    private const uint GlCompressedRgb8Etc2 = 0x9274;
    private const uint GlCompressedRgbaAstc8x8 = 0x93B7;

    // Big-endian format/type pairs (glInternalFormat, glType, glTypeSize).
    private const uint GlR32F = 0x822E;
    private const uint GlTypeFloat = 0x1406;
    private const uint GlRgb5A1 = 0x8057;
    private const uint GlTypeUShort5551 = 0x8034;
    private const uint GlRg8 = 0x822B;
    private const uint GlTypeUByte = 0x1401;

    [Fact]
    public void ReadsRgba8Surface()
    {
        var file = KtxTestFile.Ktx1(GlRgba8, 4, 4, [KtxTestFile.Rgba8(4, 4, 10, 20, 30, 40)]);
        var tex = KtxReader.Read(file);

        Assert.Equal(TextureFormat.Rgba8Unorm, tex.Format);
        Assert.Equal(TextureKind.Texture2D, tex.Kind);
        Assert.Equal(4, tex.Width);
        Assert.Equal(4, tex.Height);
        Assert.Equal(1, tex.MipLevels);
        Assert.Single(tex.Subresources);
        Assert.Equal(4 * 4 * 4, tex.Subresources[0].Data.Length);
    }

    [Fact]
    public void ReadsBc1Surface()
    {
        var file = KtxTestFile.Ktx1(GlCompressedRgbaDxt1, 4, 4, [KtxTestFile.RedBc1Block()]);
        var tex = KtxReader.Read(file);

        Assert.Equal(TextureFormat.Bc1RgbaUnorm, tex.Format);
        Assert.Equal(8, tex.Subresources[0].Data.Length);
    }

    [Fact]
    public void ReadsMipChainWithCorrectDimensions()
    {
        // 4x4 -> 4x4 (8) + 2x2 (8) + 1x1 (8) of BC1.
        var mips = new[] { KtxTestFile.RedBc1Block(), KtxTestFile.RedBc1Block(), KtxTestFile.RedBc1Block() };
        var tex = KtxReader.Read(KtxTestFile.Ktx1(GlCompressedRgbaDxt1, 4, 4, mips));

        Assert.Equal(3, tex.MipLevels);
        Assert.Equal(3, tex.Subresources.Count);
        Assert.Equal((4, 4), (tex.Subresources[0].Width, tex.Subresources[0].Height));
        Assert.Equal((2, 2), (tex.Subresources[1].Width, tex.Subresources[1].Height));
        Assert.Equal((1, 1), (tex.Subresources[2].Width, tex.Subresources[2].Height));
    }

    [Fact]
    public void ReadsCubemap()
    {
        var tex = KtxReader.Read(KtxTestFile.Ktx1Cubemap(GlCompressedRgbaDxt1, 4, 4, KtxTestFile.RedBc1Block()));

        Assert.Equal(TextureKind.Cube, tex.Kind);
        Assert.Equal(6, tex.Subresources.Count);
        Assert.Equal([0, 1, 2, 3, 4, 5], tex.Subresources.Select(s => s.Face).ToArray());
    }

    [Fact]
    public void DecodesBaseSurfaceThroughTextureData()
    {
        var tex = KtxReader.Read(KtxTestFile.Ktx1(GlCompressedRgbaDxt1, 4, 4, [KtxTestFile.RedBc1Block()]));
        var sr = tex.Subresources[0];

        var dst = new byte[TextureData.DecodedByteSize(sr)];
        tex.Decode(sr, dst);

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, dst[..4]); // top-left red
    }

    [Fact]
    public void ExposesGlFormatName()
    {
        var tex = KtxReader.Read(KtxTestFile.Ktx1(GlCompressedRgbaBptcUnorm, 4, 4, [new byte[16]]));
        Assert.Equal(TextureFormat.Bc7Unorm, tex.Format);
        Assert.Equal("GL_COMPRESSED_RGBA_BPTC_UNORM", tex.FormatName);
    }

    [Fact]
    public void DefaultsToBottomLeftOrigin()
    {
        // No KTXorientation metadata: KTX1 follows the OpenGL bottom-up convention.
        var tex = KtxReader.Read(KtxTestFile.Ktx1(GlRgba8, 4, 4, [KtxTestFile.Rgba8(4, 4, 1, 2, 3, 4)]));
        Assert.Equal(TextureOrigin.BottomLeft, tex.Origin);
    }

    [Fact]
    public void HonorsTopLeftOrientationMetadata()
    {
        var tex = KtxReader.Read(KtxTestFile.Ktx1(GlRgba8, 4, 4, [KtxTestFile.Rgba8(4, 4, 1, 2, 3, 4)], orientation: "S=r,T=d"));
        Assert.Equal(TextureOrigin.TopLeft, tex.Origin);
    }

    [Fact]
    public void ReadsAndDecodesEtc2()
    {
        // An ETC1/ETC2 individual-mode solid block (RGB444 8,8,8; index 0 -> +2) decodes to 138.
        byte[] block = [0x88, 0x88, 0x88, 0x00, 0x00, 0x00, 0x00, 0x00];
        var tex = KtxReader.Read(KtxTestFile.Ktx1(GlCompressedRgb8Etc2, 4, 4, [block]));

        Assert.Equal(TextureFormat.Etc2Rgb8Unorm, tex.Format);
        Assert.Equal("GL_COMPRESSED_RGB8_ETC2", tex.FormatName);

        var dst = new byte[TextureData.DecodedByteSize(tex.Subresources[0])];
        tex.Decode(tex.Subresources[0], dst);
        Assert.Equal(new byte[] { 138, 138, 138, 255 }, dst[..4]);
    }

    [Fact]
    public void ReadsAndDecodesR8()
    {
        // GL_R8 (0x8229): uncompressed single-channel, shown as grayscale. The 2-byte row is padded to
        // 4 per the KTX1 unpack alignment.
        var tex = KtxReader.Read(KtxTestFile.Ktx1(0x8229, 2, 1, [[100, 200, 0, 0]]));
        Assert.Equal(TextureFormat.R8Unorm, tex.Format);
        Assert.Equal("GL_R8", tex.FormatName);

        var dst = new byte[TextureData.DecodedByteSize(tex.Subresources[0])];
        tex.Decode(tex.Subresources[0], dst);
        Assert.Equal(new byte[] { 100, 100, 100, 255 }, dst[..4]);
    }

    [Fact]
    public void ReadsAndDecodesR8Integer()
    {
        // GL_R8UI (0x8232) shows integer values directly; GL_R8I (0x8231) uses the signed remap.
        var uintTex = KtxReader.Read(KtxTestFile.Ktx1(0x8232, 2, 1, [[100, 200, 0, 0]]));
        Assert.Equal(TextureFormat.R8Uint, uintTex.Format);
        Assert.Equal("GL_R8UI", uintTex.FormatName);
        var uintDst = new byte[TextureData.DecodedByteSize(uintTex.Subresources[0])];
        uintTex.Decode(uintTex.Subresources[0], uintDst);
        Assert.Equal(new byte[] { 100, 100, 100, 255 }, uintDst[..4]);

        var sintTex = KtxReader.Read(KtxTestFile.Ktx1(0x8231, 2, 1, [[127, 0x80, 0, 0]]));
        Assert.Equal(TextureFormat.R8Sint, sintTex.Format);
        Assert.Equal("GL_R8I", sintTex.FormatName);
        var sintDst = new byte[TextureData.DecodedByteSize(sintTex.Subresources[0])];
        sintTex.Decode(sintTex.Subresources[0], sintDst);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, sintDst[..4]); // 127 -> 255
    }

    [Fact]
    public void ReadsBigEndianR32Float()
    {
        // 1x1 R32F (glTypeSize 4) holding 2.5f. The reader must swap the 4-byte component back to the
        // host's little-endian order so the float surface is correct.
        var le = BitConverter.GetBytes(2.5f);
        var tex = KtxReader.Read(KtxTestFile.Ktx1BigEndian(GlR32F, GlTypeFloat, 4, 1, 1, [le]));

        Assert.Equal(TextureFormat.R32Float, tex.Format);
        Assert.Equal("GL_R32F", tex.FormatName);
        Assert.Equal(le, tex.Subresources[0].Data.ToArray());
    }

    [Fact]
    public void ReadsBigEndianRgb5A1WithUnpadAndSwap()
    {
        // 1x1 R5G5B5A1 (glTypeSize 2) red = 0xF801. The row is 2 bytes padded to 4, so this exercises
        // row-unpadding and the 2-byte swap together (the path every small mip of a real BE file hits).
        byte[] redPadded = [0x01, 0xF8, 0x00, 0x00]; // little-endian 0xF801 + 2 pad bytes
        var tex = KtxReader.Read(KtxTestFile.Ktx1BigEndian(GlRgb5A1, GlTypeUShort5551, 2, 1, 1, [redPadded]));

        Assert.Equal(TextureFormat.Rgb5A1Unorm, tex.Format);
        var dst = new byte[TextureData.DecodedByteSize(tex.Subresources[0])];
        tex.Decode(tex.Subresources[0], dst);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, dst[..4]);
    }

    [Fact]
    public void ReadsBigEndianRg8WithoutSwapping()
    {
        // R8G8 (glTypeSize 1) is endian-neutral: even in a big-endian file the bytes pass through.
        byte[] surface = [50, 150, 0, 0]; // 2x1: pixel0 = (50,150), pixel1 = (0,0)
        var tex = KtxReader.Read(KtxTestFile.Ktx1BigEndian(GlRg8, GlTypeUByte, 1, 2, 1, [surface]));

        Assert.Equal(TextureFormat.Rg8Unorm, tex.Format);
        var dst = new byte[TextureData.DecodedByteSize(tex.Subresources[0])];
        tex.Decode(tex.Subresources[0], dst);
        Assert.Equal(new byte[] { 50, 150, 0, 255 }, dst[..4]);
    }

    [Fact]
    public void ReadsBigEndianMipChain()
    {
        // 2x2 R5G5B5A1 with a 1x1 second mip, all red. Each mip is swapped independently and the 1x1
        // mip carries row padding, so the whole chain must unpad-and-swap per level.
        byte[] mip0 = [0x01, 0xF8, 0x01, 0xF8, 0x01, 0xF8, 0x01, 0xF8]; // 2x2 red
        byte[] mip1 = [0x01, 0xF8, 0x00, 0x00];                          // 1x1 red + pad
        var tex = KtxReader.Read(KtxTestFile.Ktx1BigEndian(GlRgb5A1, GlTypeUShort5551, 2, 2, 2, [mip0, mip1]));

        Assert.Equal(2, tex.MipLevels);
        foreach (var sr in tex.Subresources)
        {
            var dst = new byte[TextureData.DecodedByteSize(sr)];
            tex.Decode(sr, dst);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, dst[..4]);
        }
    }

    [Fact]
    public void RejectsInvalidEndiannessMarker()
    {
        var file = KtxTestFile.Ktx1(GlRgba8, 4, 4, [KtxTestFile.Rgba8(4, 4, 0, 0, 0, 0)]);
        file[12] = 0xDE;
        file[13] = 0xAD; // neither 0x04030201 nor 0x01020304
        Assert.Throws<NotSupportedException>(() => KtxReader.Read(file));
    }

    [Fact]
    public void RejectsBigEndianWithInvalidGlTypeSize()
    {
        // glTypeSize 3 is not a valid GL type size; a big-endian file claiming it is malformed.
        Assert.Throws<InvalidDataException>(
            () => KtxReader.Read(KtxTestFile.Ktx1BigEndian(GlR32F, GlTypeFloat, 3, 1, 1, [new byte[4]])));
    }

    [Fact]
    public void UnpadsRowAlignedUncompressed()
    {
        // KTX1 pads each uncompressed row to 4 bytes. A 3-wide R8 row (3 bytes) is stored as 4; the
        // reader must strip that so the surface is tight (3x2 = 6 bytes) and decodes correctly.
        byte[] padded = [10, 11, 12, 0, /* row 0 + pad */ 20, 21, 22, 0 /* row 1 + pad */];
        var tex = KtxReader.Read(KtxTestFile.Ktx1(0x8229, 3, 2, [padded]));

        Assert.Equal(TextureFormat.R8Unorm, tex.Format);
        var sr = tex.Subresources[0];
        Assert.Equal(6, sr.Data.Length); // unpadded to 3*2

        var dst = new byte[TextureData.DecodedByteSize(sr)];
        tex.Decode(sr, dst);
        Assert.Equal(new byte[] { 10, 10, 10, 255 }, dst[..4]);          // (0,0)
        Assert.Equal(new byte[] { 20, 20, 20, 255 }, dst[(3 * 4)..(4 * 4)]); // (0,1) - second row
    }

    [Fact]
    public void ReadsAstcStructure()
    {
        // An 8x8 surface with the 8x8 ASTC footprint is a single 128-bit block. Decode is native, so
        // the reader only resolves identity/size here.
        var tex = KtxReader.Read(KtxTestFile.Ktx1(GlCompressedRgbaAstc8x8, 8, 8, [new byte[16]]));

        Assert.Equal(TextureFormat.Astc8x8Unorm, tex.Format);
        Assert.Equal("GL_COMPRESSED_RGBA_ASTC_8x8_KHR", tex.FormatName);
        Assert.Equal(16, tex.Subresources[0].Data.Length);
    }

    [Fact]
    public void RejectsBadMagic()
    {
        var file = KtxTestFile.Ktx1(GlRgba8, 4, 4, [KtxTestFile.Rgba8(4, 4, 0, 0, 0, 0)]);
        file[1] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => KtxReader.Read(file));
    }

    [Fact]
    public void RejectsTruncatedSurface()
    {
        // Header claims a 4x4 BC1 level (8 bytes) but only 4 bytes of payload are present.
        var file = KtxTestFile.Ktx1(GlCompressedRgbaDxt1, 4, 4, [new byte[4]]);
        Assert.Throws<InvalidDataException>(() => KtxReader.Read(file));
    }

    [Fact]
    public void RejectsImplausibleMipCount()
    {
        // 4x4 allows at most 3 mips; fabricate a header claiming 99.
        var file = KtxTestFile.Ktx1(GlCompressedRgbaDxt1, 4, 4, [KtxTestFile.RedBc1Block()]);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(56, 4), 99);
        Assert.Throws<InvalidDataException>(() => KtxReader.Read(file));
    }

    [Fact]
    public void RejectsTooSmallFile()
    {
        Assert.Throws<InvalidDataException>(() => KtxReader.Read(new byte[32]));
    }
}
