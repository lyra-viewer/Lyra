using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Texture.Blocks;

/// <summary>
/// Decodes individual 4x4 BCn (S3TC / RGTC) blocks to 16 RGBA8 pixels in row-major order. Each
/// method writes exactly 64 bytes (16 px * 4) into <paramref name="dst"/>. Interpolated endpoints
/// use round-to-nearest division; BCn is not bit-exact across GPUs, so this matches hardware closely
/// without claiming a single canonical result.
/// </summary>
internal static class BcBlockDecoder
{
    /// <summary>BC1 (DXT1): 8-byte block, RGB + 1-bit punch-through alpha.</summary>
    public static void DecodeBc1(ReadOnlySpan<byte> block, Span<byte> dst)
    {
        Span<byte> palette = stackalloc byte[16]; // 4 colours * RGBA
        BuildBc1Palette(block, palette, punchThroughAlpha: true);

        var indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
        for (var i = 0; i < 16; i++)
        {
            var sel = (int)((indices >> (2 * i)) & 0x3);
            palette.Slice(sel * 4, 4).CopyTo(dst.Slice(i * 4, 4));
        }
    }

    /// <summary>BC2 (DXT3): 16-byte block, explicit 4-bit alpha + BC1-style color (always 4-color).</summary>
    public static void DecodeBc2(ReadOnlySpan<byte> block, Span<byte> dst)
    {
        Span<byte> palette = stackalloc byte[16];
        BuildBc1Palette(block[8..], palette, punchThroughAlpha: false);

        var indices = BinaryPrimitives.ReadUInt32LittleEndian(block[12..]);
        for (var i = 0; i < 16; i++)
        {
            var sel = (int)((indices >> (2 * i)) & 0x3);
            palette.Slice(sel * 4, 3).CopyTo(dst.Slice(i * 4, 3));

            // Explicit 4-bit alpha: two pixels per byte, low nibble first.
            var alphaByte = block[i / 2];
            var alpha4 = (i & 1) == 0 ? alphaByte & 0x0F : alphaByte >> 4;
            dst[(i * 4) + 3] = (byte)((alpha4 << 4) | alpha4);
        }
    }

    /// <summary>BC3 (DXT5): 16-byte block, interpolated alpha + BC1-style color (always 4-color).</summary>
    public static void DecodeBc3(ReadOnlySpan<byte> block, Span<byte> dst)
    {
        Span<byte> alpha = stackalloc byte[16];
        DecodeChannelBlock(block[..8], signed: false, alpha);

        Span<byte> palette = stackalloc byte[16];
        BuildBc1Palette(block[8..], palette, punchThroughAlpha: false);

        var indices = BinaryPrimitives.ReadUInt32LittleEndian(block[12..]);
        for (var i = 0; i < 16; i++)
        {
            var sel = (int)((indices >> (2 * i)) & 0x3);
            palette.Slice(sel * 4, 3).CopyTo(dst.Slice(i * 4, 3));
            dst[(i * 4) + 3] = alpha[i];
        }
    }

    /// <summary>BC4 (RGTC1): 8-byte single-channel block, shown as grayscale RGBA.</summary>
    public static void DecodeBc4(ReadOnlySpan<byte> block, Span<byte> dst, bool signed)
    {
        Span<byte> channel = stackalloc byte[16];
        DecodeChannelBlock(block, signed, channel);

        for (var i = 0; i < 16; i++)
        {
            var v = channel[i];
            dst[(i * 4) + 0] = v;
            dst[(i * 4) + 1] = v;
            dst[(i * 4) + 2] = v;
            dst[(i * 4) + 3] = 255;
        }
    }

    /// <summary>BC5 (RGTC2): 16-byte two-channel block, red+green (blue 0), e.g. tangent-space normals.</summary>
    public static void DecodeBc5(ReadOnlySpan<byte> block, Span<byte> dst, bool signed)
    {
        Span<byte> red = stackalloc byte[16];
        Span<byte> green = stackalloc byte[16];
        DecodeChannelBlock(block[..8], signed, red);
        DecodeChannelBlock(block[8..], signed, green);

        for (var i = 0; i < 16; i++)
        {
            dst[(i * 4) + 0] = red[i];
            dst[(i * 4) + 1] = green[i];
            dst[(i * 4) + 2] = 0;
            dst[(i * 4) + 3] = 255;
        }
    }

    // ------------------------------------------------------------------
    //  Shared helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the 4-entry RGBA color palette from a BC1-style color block (first 8 bytes).
    /// When <paramref name="punchThroughAlpha"/> and colour0 &lt;= colour1, the 4th entry is the
    /// transparent-black "punch-through" used by BC1; BC2/BC3 always use the opaque 4-color mode.
    /// </summary>
    private static void BuildBc1Palette(ReadOnlySpan<byte> block, Span<byte> palette, bool punchThroughAlpha)
    {
        var c0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);

        Unpack565(c0, palette[..4]);
        Unpack565(c1, palette.Slice(4, 4));

        if (c0 > c1 || !punchThroughAlpha)
        {
            // Two interpolated colors, both opaque.
            for (var ch = 0; ch < 3; ch++)
            {
                palette[8 + ch] = (byte)(((2 * palette[ch]) + palette[4 + ch] + 1) / 3);
                palette[12 + ch] = (byte)((palette[ch] + (2 * palette[4 + ch]) + 1) / 3);
            }

            palette[11] = 255;
            palette[15] = 255;
        }
        else
        {
            // One interpolated color plus transparent black.
            for (var ch = 0; ch < 3; ch++)
            {
                palette[8 + ch] = (byte)((palette[ch] + palette[4 + ch] + 1) / 2);
                palette[12 + ch] = 0;
            }

            palette[11] = 255;
            palette[15] = 0;
        }

        palette[3] = 255;
        palette[7] = 255;
    }

    private static void Unpack565(ushort value, Span<byte> rgba)
    {
        var r = (value >> 11) & 0x1F;
        var g = (value >> 5) & 0x3F;
        var b = value & 0x1F;

        rgba[0] = (byte)((r << 3) | (r >> 2));
        rgba[1] = (byte)((g << 2) | (g >> 4));
        rgba[2] = (byte)((b << 3) | (b >> 2));
        rgba[3] = 255;
    }

    /// <summary>
    /// Decodes an 8-byte BC3-alpha/BC4/BC5 channel block (two endpoints + 3-bit-per-pixel indices)
    /// into 16 display bytes. Signed (snorm) blocks are reconstructed in [-127,127] and mapped to
    /// [0,255] for display; unsigned blocks pass their 0..255 values straight through.
    /// </summary>
    private static void DecodeChannelBlock(ReadOnlySpan<byte> block, bool signed, Span<byte> dst)
    {
        Span<int> palette = stackalloc int[8];

        if (signed)
        {
            int e0 = Clamp127((sbyte)block[0]);
            int e1 = Clamp127((sbyte)block[1]);
            palette[0] = e0;
            palette[1] = e1;

            if (e0 > e1)
            {
                for (var k = 1; k <= 6; k++)
                    palette[k + 1] = (((7 - k) * e0) + (k * e1) + 3) / 7;
            }
            else
            {
                for (var k = 1; k <= 4; k++)
                    palette[k + 1] = (((5 - k) * e0) + (k * e1) + 2) / 5;
                
                palette[6] = -127;
                palette[7] = 127;
            }
        }
        else
        {
            int e0 = block[0];
            int e1 = block[1];
            palette[0] = e0;
            palette[1] = e1;

            if (e0 > e1)
            {
                for (var k = 1; k <= 6; k++)
                    palette[k + 1] = (((7 - k) * e0) + (k * e1) + 3) / 7;
            }
            else
            {
                for (var k = 1; k <= 4; k++)
                    palette[k + 1] = (((5 - k) * e0) + (k * e1) + 2) / 5;
                
                palette[6] = 0;
                palette[7] = 255;
            }
        }

        // 16 indices * 3 bits = 48 bits packed little-endian in the trailing 6 bytes.
        ulong bits = 0;
        for (var b = 0; b < 6; b++)
            bits |= (ulong)block[2 + b] << (8 * b);

        for (var i = 0; i < 16; i++)
        {
            var sel = (int)((bits >> (3 * i)) & 0x7);
            var v = palette[sel];
            dst[i] = signed ? (byte)((((v + 127) * 255) + 127) / 254) : (byte)v;
        }
    }

    private static int Clamp127(int signedByte) => signedByte < -127 ? -127 : signedByte;
}