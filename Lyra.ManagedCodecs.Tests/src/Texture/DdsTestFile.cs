using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Tests.Texture;

/// <summary>Builds minimal in-memory .dds files (header + raw payload) for reader tests.</summary>
internal static class DdsTestFile
{
    public static byte[] Legacy(int width, int height, int mipCount, string fourCc, byte[] data)
        => Build(width, height, mipCount, dx10: false, FourCc(fourCc), dxgi: 0, caps2: 0, data);

    /// <summary>Legacy header with a numeric D3DFORMAT in the FourCC field (e.g. 113 = RGBA16F).</summary>
    public static byte[] LegacyNumericFourCc(int width, int height, int mipCount, uint fourCc, byte[] data)
        => Build(width, height, mipCount, dx10: false, fourCc, dxgi: 0, caps2: 0, data);

    /// <summary>Legacy header with explicit pixel-format flags and channel masks (RGB / BUMPDUDV).</summary>
    public static byte[] Masked(int width, int height, uint pfFlags, uint bitCount, uint r, uint g, uint b, uint a, byte[] data)
    {
        var buf = new byte[4 + 124 + data.Length];
        W32(buf, 0, 0x20534444);
        W32(buf, 4, 124);
        W32(buf, 8, 0x1007);
        W32(buf, 12, (uint)height);
        W32(buf, 16, (uint)width);
        W32(buf, 28, 1);
        W32(buf, 76, 32); // pfSize
        W32(buf, 80, pfFlags);
        W32(buf, 88, bitCount);
        W32(buf, 92, r);
        W32(buf, 96, g);
        W32(buf, 100, b);
        W32(buf, 104, a);
        W32(buf, 108, 0x1000);
        data.CopyTo(buf, 4 + 124);
        return buf;
    }

    public static byte[] Dx10(int width, int height, int mipCount, uint dxgiFormat, byte[] data, uint resourceDimension = 3, uint miscFlag = 0, uint arraySize = 1, uint depth = 0, uint caps2 = 0)
        => Build(width, height, mipCount, dx10: true, fourCc: 0, dxgi: dxgiFormat, caps2: caps2, data, resourceDimension, miscFlag, arraySize, depth);

    private static byte[] Build(int width, int height, int mipCount, bool dx10, uint fourCc, uint dxgi, uint caps2, byte[] data, uint resourceDimension = 3, uint miscFlag = 0, uint arraySize = 1, uint depth = 0)
    {
        var headerEnd = 4 + 124 + (dx10 ? 20 : 0);
        var buf = new byte[headerEnd + data.Length];

        W32(buf, 0, 0x20534444);                             // "DDS "
        W32(buf, 4, 124);                                    // dwSize
        W32(buf, 8, 0x1007 | (mipCount > 1 ? 0x20000u : 0)); // CAPS|HEIGHT|WIDTH|PIXELFORMAT [|MIPMAPCOUNT]
        W32(buf, 12, (uint)height);
        W32(buf, 16, (uint)width);
        W32(buf, 24, depth);
        W32(buf, 28, (uint)mipCount);

        // DDS_PIXELFORMAT at header offset 72 (file offset 76).
        W32(buf, 76, 32);       // pfSize
        W32(buf, 80, 0x4);      // DDPF_FOURCC
        W32(buf, 84, dx10 ? FourCc("DX10") : fourCc);

        W32(buf, 108, 0x1000);  // dwCaps = TEXTURE
        W32(buf, 112, caps2);   // dwCaps2

        if (dx10)
        {
            W32(buf, 128, dxgi);
            W32(buf, 132, resourceDimension);
            W32(buf, 136, miscFlag);
            W32(buf, 140, arraySize);
            W32(buf, 144, 0);
        }

        data.CopyTo(buf, headerEnd);
        return buf;
    }

    /// <summary>A solid-red BC1 4x4 block (color0 = red565, all indices 0).</summary>
    public static byte[] RedBc1Block() => [0x00, 0xF8, 0x00, 0xF8, 0x00, 0x00, 0x00, 0x00];

    private static uint FourCc(string code)
        => (uint)(code[0] | (code[1] << 8) | (code[2] << 16) | (code[3] << 24));

    private static void W32(byte[] buf, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), value);
}