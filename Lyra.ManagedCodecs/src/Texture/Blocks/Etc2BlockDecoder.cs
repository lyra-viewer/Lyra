using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Texture.Blocks;

/// <summary>
/// Decodes ETC2 / EAC 4x4 blocks into 8-bit RGBA. ETC2 RGB supports all five sub-modes (individual,
/// differential, T, H, planar) plus RGB8A1 punch-through alpha; the RGBA8 variant pairs an EAC alpha
/// block with an ETC2 colour block, and EAC R11/RG11 carry one or two 11-bit channels (shown as
/// grayscale / red+green, matching the BC4/BC5 convention).
///
/// The bit layout follows the Khronos reference decode: each 64-bit block splits into two words read
/// big-endian from its byte halves - <c>cHigh</c> (bytes 0-3) holds the colour/mode fields and
/// <c>cLow</c> (bytes 4-7) the per-pixel index planes. Pixel (x=col, y=row) is the linear index
/// <c>4*x + y</c>; its 2-bit selector is the LSB plane bit at that position and the MSB plane bit 16
/// higher.
/// </summary>
internal static class Etc2BlockDecoder
{
    // Per-codeword ETC1 intensity pair {small, large}; the sign comes from the pixel's MSB plane.
    private static readonly int[,] Etc1Modifier =
    {
        { 2, 8 },   { 5, 17 },  { 9, 29 },   { 13, 42 },
        { 18, 60 }, { 24, 80 }, { 33, 106 }, { 47, 183 },
    };

    // EAC modifier magnitudes (4 per row, indexed by the low 2 selector bits); negatives are the
    // bitwise complement, selected by the MSB plane bit - this reproduces the 8-entry signed table.
    private static readonly int[,] EacModifier =
    {
        { 2, 5, 8, 14 }, { 2, 6, 9, 12 }, { 1, 4, 7, 12 }, { 1, 3, 5, 12 },
        { 2, 5, 7, 11 }, { 2, 6, 8, 10 }, { 3, 6, 7, 10 }, { 2, 4, 7, 10 },
        { 1, 5, 7, 9 },  { 1, 4, 7, 9 },  { 1, 3, 7, 9 },  { 1, 4, 6, 9 },
        { 2, 3, 6, 9 },  { 0, 1, 2, 9 },  { 3, 5, 7, 8 },  { 2, 4, 6, 8 },
    };

    private static readonly int[] DistanceTable = [3, 6, 11, 16, 23, 32, 41, 64];

    /// <summary>ETC2 RGB (8-byte block). <paramref name="punchThrough"/> selects the RGB8A1 variant.</summary>
    public static void DecodeRgb(ReadOnlySpan<byte> block, Span<byte> dst, bool punchThrough)
    {
        var cHigh = BinaryPrimitives.ReadUInt32BigEndian(block);
        var cLow = BinaryPrimitives.ReadUInt32BigEndian(block[4..]);
        DecodeColor(cHigh, cLow, dst, punchThrough);
    }

    /// <summary>ETC2 RGBA8 (16-byte block): EAC alpha in the first 8 bytes, ETC2 colour in the last 8.</summary>
    public static void DecodeRgba8(ReadOnlySpan<byte> block, Span<byte> dst)
    {
        var aHigh = BinaryPrimitives.ReadUInt32BigEndian(block);
        var aLow = BinaryPrimitives.ReadUInt32BigEndian(block[4..]);
        var cHigh = BinaryPrimitives.ReadUInt32BigEndian(block[8..]);
        var cLow = BinaryPrimitives.ReadUInt32BigEndian(block[12..]);

        DecodeColor(cHigh, cLow, dst, punchThrough: false);

        for (var x = 0; x < 4; x++)
        for (var y = 0; y < 4; y++)
        {
            var lp = (4 * x) + y;
            dst[(((y * 4) + x) * 4) + 3] = (byte)DecodeEacAlpha(aHigh, aLow, lp);
        }
    }

    /// <summary>EAC R11 (8-byte block), shown as grayscale (matching BC4).</summary>
    public static void DecodeEacR11(ReadOnlySpan<byte> block, Span<byte> dst, bool signed)
    {
        var high = BinaryPrimitives.ReadUInt32BigEndian(block);
        var low = BinaryPrimitives.ReadUInt32BigEndian(block[4..]);

        for (var x = 0; x < 4; x++)
        for (var y = 0; y < 4; y++)
        {
            var lp = (4 * x) + y;
            var v = DecodeEac11To8(high, low, lp, signed);
            var i = (((y * 4) + x) * 4);
            dst[i + 0] = v;
            dst[i + 1] = v;
            dst[i + 2] = v;
            dst[i + 3] = 255;
        }
    }

    /// <summary>EAC RG11 (16-byte block): red in the first 8 bytes, green in the last 8 (matching BC5).</summary>
    public static void DecodeEacRg11(ReadOnlySpan<byte> block, Span<byte> dst, bool signed)
    {
        var rHigh = BinaryPrimitives.ReadUInt32BigEndian(block);
        var rLow = BinaryPrimitives.ReadUInt32BigEndian(block[4..]);
        var gHigh = BinaryPrimitives.ReadUInt32BigEndian(block[8..]);
        var gLow = BinaryPrimitives.ReadUInt32BigEndian(block[12..]);

        for (var x = 0; x < 4; x++)
        for (var y = 0; y < 4; y++)
        {
            var lp = (4 * x) + y;
            var i = (((y * 4) + x) * 4);
            dst[i + 0] = DecodeEac11To8(rHigh, rLow, lp, signed);
            dst[i + 1] = DecodeEac11To8(gHigh, gLow, lp, signed);
            dst[i + 2] = 0;
            dst[i + 3] = 255;
        }
    }

    // ------------------------------------------------------------------
    //  ETC2 colour
    // ------------------------------------------------------------------

    private static void DecodeColor(uint cHigh, uint cLow, Span<byte> dst, bool punchThrough)
    {
        var diff = (cHigh & 2u) != 0u;

        // For RGB8A1 the diff bit is repurposed as the opaque flag; punch-through is active when it is 0.
        var punchActive = punchThrough && !diff;
        var individual = !punchThrough && !diff;

        var r5 = (int)((cHigh >> 27) & 0x1F);
        var rd = SignExtend((int)((cHigh >> 24) & 0x7), 3);
        var g5 = (int)((cHigh >> 19) & 0x1F);
        var gd = SignExtend((int)((cHigh >> 16) & 0x7), 3);
        var b5 = (int)((cHigh >> 11) & 0x1F);
        var bd = SignExtend((int)((cHigh >> 8) & 0x7), 3);

        switch (individual)
        {
            case false when (uint)(r5 + rd) > 31:
                DecodeTMode(cHigh, cLow, dst, punchActive);
                break;
            case false when (uint)(g5 + gd) > 31:
                DecodeHMode(cHigh, cLow, dst, punchActive);
                break;
            case false when (uint)(b5 + bd) > 31:
                DecodePlanar(cHigh, cLow, dst);
                break;
            default:
                DecodeEtc1(cHigh, cLow, dst, individual, r5, g5, b5, rd, gd, bd, punchActive);
                break;
        }
    }

    /// <summary>ETC1-compatible individual / differential mode (per-subblock base + intensity modifier).</summary>
    private static void DecodeEtc1(
        uint cHigh, uint cLow, Span<byte> dst, bool individual,
        int r5, int g5, int b5, int rd, int gd, int bd, bool punchActive)
    {
        var flip = cHigh & 1u;

        Span<int> base0 = stackalloc int[3];
        Span<int> base1 = stackalloc int[3];

        if (individual)
        {
            base0[0] = Expand4((int)((cHigh >> 28) & 0xF));
            base0[1] = Expand4((int)((cHigh >> 20) & 0xF));
            base0[2] = Expand4((int)((cHigh >> 12) & 0xF));
            base1[0] = Expand4((int)((cHigh >> 24) & 0xF));
            base1[1] = Expand4((int)((cHigh >> 16) & 0xF));
            base1[2] = Expand4((int)((cHigh >> 8) & 0xF));
        }
        else
        {
            base0[0] = Expand5(r5);
            base0[1] = Expand5(g5);
            base0[2] = Expand5(b5);
            base1[0] = Expand5(r5 + rd);
            base1[1] = Expand5(g5 + gd);
            base1[2] = Expand5(b5 + bd);
        }

        for (var x = 0; x < 4; x++)
        for (var y = 0; y < 4; y++)
        {
            var subblock = ((flip == 0 ? x : y) & 2) >> 1;
            var baseRgb = subblock == 0 ? base0 : base1;
            var table = subblock == 0 ? (int)((cHigh >> 5) & 0x7) : (int)((cHigh >> 2) & 0x7);

            var lp = (4 * x) + y;
            var msb = (int)((cLow >> (15 + lp)) & 2);
            var lsb = (int)((cLow >> lp) & 1);

            var sign = 1 - msb; // msb is 0 or 2 -> +1 or -1
            var transparent = false;
            if (punchActive)
            {
                sign *= lsb;
                transparent = (msb + lsb) == 2; // msb set, lsb clear
            }

            var offset = Etc1Modifier[table, lsb] * sign;
            Store(dst, x, y, Clamp(baseRgb[0] + offset), Clamp(baseRgb[1] + offset), Clamp(baseRgb[2] + offset), transparent);
        }
    }

    private static void DecodeTMode(uint cHigh, uint cLow, Span<byte> dst, bool punchActive)
    {
        var r1 = (int)((cHigh >> 24) & 0x3) | ((int)((cHigh >> 27) & 0x3) << 2);
        var g1 = (int)((cHigh >> 20) & 0xF);
        var b1 = (int)((cHigh >> 16) & 0xF);
        var r2 = (int)((cHigh >> 12) & 0xF);
        var g2 = (int)((cHigh >> 8) & 0xF);
        var b2 = (int)((cHigh >> 4) & 0xF);
        var da = ((int)((cHigh >> 2) & 0x3) << 1) | (int)(cHigh & 1);
        var dist = DistanceTable[da];

        var paint1R = Expand4(r1);
        var paint1G = Expand4(g1);
        var paint1B = Expand4(b1);
        var p2R = Expand4(r2);
        var p2G = Expand4(g2);
        var p2B = Expand4(b2);

        for (var x = 0; x < 4; x++)
        for (var y = 0; y < 4; y++)
        {
            var lp = (4 * x) + y;
            var index = (int)((cLow >> (15 + lp)) & 2) | (int)((cLow >> lp) & 1);
            var transparent = punchActive && index == 2;

            if (index == 0)
            {
                Store(dst, x, y, paint1R, paint1G, paint1B, transparent);
            }
            else
            {
                var mod = 2 - index; // +dist, 0, -dist for indices 1,2,3
                Store(dst, x, y, Clamp(p2R + (mod * dist)), Clamp(p2G + (mod * dist)), Clamp(p2B + (mod * dist)), transparent);
            }
        }
    }

    private static void DecodeHMode(uint cHigh, uint cLow, Span<byte> dst, bool punchActive)
    {
        var r1 = (int)((cHigh >> 27) & 0xF);
        var g1 = ((int)((cHigh >> 24) & 0x7) << 1) | (int)((cHigh >> 20) & 1);
        var b1 = (int)((cHigh >> 15) & 0x7) | (int)((cHigh >> 16) & 0x8);
        var r2 = (int)((cHigh >> 11) & 0xF);
        var g2 = (int)((cHigh >> 7) & 0xF);
        var b2 = (int)((cHigh >> 3) & 0xF);

        var da = (int)(cHigh & 4);
        var db = (int)(cHigh & 1);
        var d = da + (2 * db);
        d += ((r1 << 16) | (g1 << 8) | b1) >= ((r2 << 16) | (g2 << 8) | b2) ? 1 : 0;
        var dist = DistanceTable[d];

        var c1R = Expand4(r1);
        var c1G = Expand4(g1);
        var c1B = Expand4(b1);
        var c2R = Expand4(r2);
        var c2G = Expand4(g2);
        var c2B = Expand4(b2);

        for (var x = 0; x < 4; x++)
        for (var y = 0; y < 4; y++)
        {
            var lp = (4 * x) + y;
            var msb = (int)((cLow >> (15 + lp)) & 2);
            var lsb = (int)((cLow >> lp) & 1);
            var transparent = punchActive && (msb + lsb) == 2;

            var useSecond = msb != 0;
            var baseR = useSecond ? c2R : c1R;
            var baseG = useSecond ? c2G : c1G;
            var baseB = useSecond ? c2B : c1B;
            var mod = 1 - (2 * lsb); // +dist for lsb 0, -dist for lsb 1

            Store(dst, x, y, Clamp(baseR + (mod * dist)), Clamp(baseG + (mod * dist)), Clamp(baseB + (mod * dist)), transparent);
        }
    }

    private static void DecodePlanar(uint cHigh, uint cLow, Span<byte> dst)
    {
        var o0 = Expand6((int)((cHigh >> 25) & 0x3F));
        var o1 = Expand7((int)((cHigh >> 17) & 0x3F) | (int)((cHigh >> 18) & 0x40));
        var o2 = Expand6((int)((cHigh >> 7) & 0x7) | ((int)((cHigh >> 11) & 0x3) << 3) | (int)((cHigh >> 11) & 0x20));

        var h0 = Expand6((int)(cHigh & 1) | ((int)((cHigh >> 2) & 0x1F) << 1));
        var h1 = Expand7((int)((cLow >> 25) & 0x7F));
        var h2 = Expand6((int)((cLow >> 19) & 0x3F));

        var v0 = Expand6((int)((cLow >> 13) & 0x3F));
        var v1 = Expand7((int)((cLow >> 6) & 0x7F));
        var v2 = Expand6((int)(cLow & 0x3F));

        var dx0 = h0 - o0;
        var dx1 = h1 - o1;
        var dx2 = h2 - o2;
        var dy0 = v0 - o0;
        var dy1 = v1 - o1;
        var dy2 = v2 - o2;

        for (var x = 0; x < 4; x++)
        for (var y = 0; y < 4; y++)
        {
            var r = o0 + (((dx0 * x) + (dy0 * y) + 2) >> 2);
            var g = o1 + (((dx1 * x) + (dy1 * y) + 2) >> 2);
            var b = o2 + (((dx2 * x) + (dy2 * y) + 2) >> 2);
            Store(dst, x, y, Clamp(r), Clamp(g), Clamp(b), transparent: false);
        }
    }

    // ------------------------------------------------------------------
    //  EAC
    // ------------------------------------------------------------------

    /// <summary>Decodes one EAC alpha texel (8-bit output, [0,255]) for the ETC2 RGBA8 alpha block.</summary>
    private static int DecodeEacAlpha(uint high, uint low, int lp)
    {
        var baseValue = (int)((high >> 24) & 0xFF);
        var multiplier = (int)((high >> 20) & 0xF);
        var table = (int)((high >> 16) & 0xF);

        var mod = EacModifierValue(high, low, lp, table);
        return Clamp(baseValue + (mod * multiplier));
    }

    /// <summary>Decodes one EAC R/RG channel texel to an 8-bit display value (11-bit internal).</summary>
    private static byte DecodeEac11To8(uint high, uint low, int lp, bool signed)
    {
        var table = (int)((high >> 16) & 0xF);
        var mod = EacModifierValue(high, low, lp, table);

        if (signed)
        {
            var sbase = (sbyte)((high >> 24) & 0xFF);
            var sb = sbase < -127 ? -127 : (int)sbase;
            var mult = Math.Max((int)((high >> 20) & 0xF) * 8, 1);
            var value = Math.Clamp((sb * 8) + (mod * mult), -1023, 1023);
            return (byte)(((value + 1023) * 255) / 2046);
        }
        else
        {
            var baseValue = ((int)((high >> 24) & 0xFF) * 8) + 4;
            var mult = Math.Max((int)((high >> 20) & 0xF) * 8, 1);
            var value = Math.Clamp(baseValue + (mod * mult), 0, 2047);
            return (byte)(((value * 255) + 1023) / 2047);
        }
    }

    /// <summary>
    /// The signed modifier for one EAC texel: a 2-bit magnitude index plus a 1-bit MSB sign plane.
    /// Negative entries are the bitwise complement of the magnitude (reconstructing the 8-entry table),
    /// matching the Khronos reference's <c>mag ^ (msb - 1)</c>.
    /// </summary>
    private static int EacModifierValue(uint high, uint low, int lp, int table)
    {
        var bitOffset = 45 - (3 * lp);
        var lsbWord = (bitOffset >> 5) == 0 ? low : high;
        var lsbIndex = (int)((lsbWord >> (bitOffset & 31)) & 3);

        var msbOffset = bitOffset + 2;
        var msbWord = (msbOffset >> 5) == 0 ? low : high;
        var msb = (int)((msbWord >> (msbOffset & 31)) & 1);

        var mag = EacModifier[table, lsbIndex];
        return msb == 1 ? mag : ~mag;
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    private static void Store(Span<byte> dst, int x, int y, int r, int g, int b, bool transparent)
    {
        var i = (((y * 4) + x) * 4);
        if (transparent)
        {
            dst[i + 0] = 0;
            dst[i + 1] = 0;
            dst[i + 2] = 0;
            dst[i + 3] = 0;
            return;
        }

        dst[i + 0] = (byte)r;
        dst[i + 1] = (byte)g;
        dst[i + 2] = (byte)b;
        dst[i + 3] = 255;
    }

    private static int SignExtend(int value, int bits)
        => (value & (1 << (bits - 1))) != 0 ? value - (1 << bits) : value;

    private static int Expand4(int v) => v * 0x11;            // 4-bit -> 8-bit (replicate nibble)
    private static int Expand5(int v) => (v << 3) | (v >> 2); // 5-bit -> 8-bit
    private static int Expand6(int v) => (v << 2) | (v >> 4); // 6-bit -> 8-bit
    private static int Expand7(int v) => (v << 1) | (v >> 6); // 7-bit -> 8-bit

    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
}
