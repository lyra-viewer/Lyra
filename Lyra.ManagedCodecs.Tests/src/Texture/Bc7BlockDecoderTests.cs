using Lyra.ManagedCodecs.Texture;
using Lyra.ManagedCodecs.Texture.Dds;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Texture;

public class Bc7BlockDecoderTests
{
    // Builds a BC7 mode-6 block with both endpoints equal, so every texel decodes to the same color
    // regardless of its index. Exercises mode parsing, endpoint expansion, P-bits and interpolation.
    [Fact]
    public void DecodesUniformMode6Block()
    {
        var w = new BitWriter();
        w.Write(1 << 6, 7); // mode 6: six zero bits then a 1

        // Endpoints are component-major: R0,R1, G0,G1, B0,B1 (7 bits), then A0,A1 (7 bits).
        // Equal endpoints (e0 == e1) make the result index-independent.
        foreach (var v in new[] { 64, 64, 32, 32, 16, 16 }) w.Write(v, 7); // RGB
        foreach (var v in new[] { 100, 100 }) w.Write(v, 7); // alpha

        w.Write(1, 1); // P-bit endpoint 0
        w.Write(1, 1); // P-bit endpoint 1

        w.Write(0, 3); // texel 0 index (anchor: 3 bits)
        for (var t = 1; t < 16; t++) w.Write(0, 4); // texels 1..15 (4 bits)

        var dst = new byte[4 * 4 * 4];
        SurfaceDecoder.DecodeSurface(TextureFormat.Bc7Unorm, w.Bytes, dst, 4, 4);

        // Each 7-bit endpoint gets its P-bit appended as the LSB, then expands to 8 bits.
        // e.g. R: (64 << 1) | 1 = 129; G: 65; B: 33; A: 201.
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(129, dst[(i * 4) + 0]);
            Assert.Equal(65, dst[(i * 4) + 1]);
            Assert.Equal(33, dst[(i * 4) + 2]);
            Assert.Equal(201, dst[(i * 4) + 3]);
        }
    }

    [Fact]
    public void DdsReaderMapsDx10Bc7()
    {
        var file = DdsTestFile.Dx10(4, 4, 1, dxgiFormat: 98, new byte[16]);
        Assert.Equal(TextureFormat.Bc7Unorm, DdsReader.Read(file).Format);

        var srgb = DdsTestFile.Dx10(4, 4, 1, dxgiFormat: 99, new byte[16]);
        Assert.Equal(TextureFormat.Bc7UnormSrgb, DdsReader.Read(srgb).Format);
    }

    /// <summary>Packs fields LSB-first into a 16-byte BC7 block, the order the decoder consumes them.</summary>
    private sealed class BitWriter
    {
        public byte[] Bytes { get; } = new byte[16];
        private int _pos;

        public void Write(int value, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (((value >> i) & 1) != 0) 
                    Bytes[_pos >> 3] |= (byte)(1 << (_pos & 7));

                _pos++;
            }
        }
    }
}