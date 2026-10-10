using System.Numerics;

namespace Lyra.ManagedCodecs.Texture.Blocks.Astc;

/// <summary>
/// The HDR half of ASTC: the six HDR color-endpoint modes, which unpack to 12-bit logarithmic values
/// widened to 16 bits, and the conversions of interpolated 16-bit values to half floats. Ported from
/// astcenc (<c>astcenc_color_unquantize.cpp</c>, <c>astcenc_vecmathlib.h</c>), whose output is the
/// reference: these match it bit for bit.
/// </summary>
internal static class AstcHdr
{
    // Mode 2: HDR luminance, large range.
    public static void LuminanceLargeRange(ReadOnlySpan<int> v, Span<int> ep0, Span<int> ep1)
    {
        int y0, y1;
        if (v[1] >= v[0])
        {
            y0 = v[0] << 4;
            y1 = v[1] << 4;
        }
        else
        {
            y0 = (v[1] << 4) + 8;
            y1 = (v[0] << 4) - 8;
        }

        Set(ep0, y0 << 4, y0 << 4, y0 << 4);
        Set(ep1, y1 << 4, y1 << 4, y1 << 4);
    }

    // Mode 3: HDR luminance, small range.
    public static void LuminanceSmallRange(ReadOnlySpan<int> v, Span<int> ep0, Span<int> ep1)
    {
        int y0, y1;
        if ((v[0] & 0x80) != 0)
        {
            y0 = ((v[1] & 0xE0) << 4) | ((v[0] & 0x7F) << 2);
            y1 = (v[1] & 0x1F) << 2;
        }
        else
        {
            y0 = ((v[1] & 0xF0) << 4) | ((v[0] & 0x7F) << 1);
            y1 = (v[1] & 0xF) << 1;
        }

        y1 = Math.Min(y1 + y0, 0xFFF);

        Set(ep0, y0 << 4, y0 << 4, y0 << 4);
        Set(ep1, y1 << 4, y1 << 4, y1 << 4);
    }

    // Mode 7: HDR RGB, base + scale.
    public static void RgbScale(ReadOnlySpan<int> v, Span<int> ep0, Span<int> ep1)
    {
        int v0 = v[0], v1 = v[1], v2 = v[2], v3 = v[3];

        var modeval = ((v0 & 0xC0) >> 6) | (((v1 & 0x80) >> 7) << 2) | (((v2 & 0x80) >> 7) << 3);

        int majcomp, mode;
        if ((modeval & 0xC) != 0xC)
        {
            majcomp = modeval >> 2;
            mode = modeval & 3;
        }
        else if (modeval != 0xF)
        {
            majcomp = modeval & 3;
            mode = 4;
        }
        else
        {
            majcomp = 0;
            mode = 5;
        }

        var red = v0 & 0x3F;
        var green = v1 & 0x1F;
        var blue = v2 & 0x1F;
        var scale = v3 & 0x1F;

        var bit0 = (v1 >> 6) & 1;
        var bit1 = (v1 >> 5) & 1;
        var bit2 = (v2 >> 6) & 1;
        var bit3 = (v2 >> 5) & 1;
        var bit4 = (v3 >> 7) & 1;
        var bit5 = (v3 >> 6) & 1;
        var bit6 = (v3 >> 5) & 1;

        var ohcomp = 1 << mode;

        if ((ohcomp & 0x30) != 0) green |= bit0 << 6;
        if ((ohcomp & 0x3A) != 0) green |= bit1 << 5;
        if ((ohcomp & 0x30) != 0) blue |= bit2 << 6;
        if ((ohcomp & 0x3A) != 0) blue |= bit3 << 5;

        if ((ohcomp & 0x3D) != 0) scale |= bit6 << 5;
        if ((ohcomp & 0x2D) != 0) scale |= bit5 << 6;
        if ((ohcomp & 0x04) != 0) scale |= bit4 << 7;

        if ((ohcomp & 0x3B) != 0) red |= bit4 << 6;
        if ((ohcomp & 0x04) != 0) red |= bit3 << 6;

        if ((ohcomp & 0x10) != 0) red |= bit5 << 7;
        if ((ohcomp & 0x0F) != 0) red |= bit2 << 7;

        if ((ohcomp & 0x05) != 0) red |= bit1 << 8;
        if ((ohcomp & 0x0A) != 0) red |= bit0 << 8;

        if ((ohcomp & 0x05) != 0) red |= bit0 << 9;
        if ((ohcomp & 0x02) != 0) red |= bit6 << 9;

        if ((ohcomp & 0x01) != 0) red |= bit3 << 10;
        if ((ohcomp & 0x02) != 0) red |= bit5 << 10;

        // Expand to 12 bits.
        ReadOnlySpan<int> shamts = [1, 1, 2, 3, 4, 5];
        var shamt = shamts[mode];
        red <<= shamt;
        green <<= shamt;
        blue <<= shamt;
        scale <<= shamt;

        // Modes 0 to 4 store green and blue as differences from red.
        if (mode != 5)
        {
            green = red - green;
            blue = red - blue;
        }

        if (majcomp == 1)
            (red, green) = (green, red);
        else if (majcomp == 2)
            (red, blue) = (blue, red);

        var red0 = Math.Max(red - scale, 0);
        var green0 = Math.Max(green - scale, 0);
        var blue0 = Math.Max(blue - scale, 0);

        Set(ep0, red0 << 4, green0 << 4, blue0 << 4);
        Set(ep1, Math.Max(red, 0) << 4, Math.Max(green, 0) << 4, Math.Max(blue, 0) << 4);
    }

    // Mode 11: HDR RGB, direct. Modes 14 and 15 add an alpha to it.
    public static void Rgb(ReadOnlySpan<int> v, Span<int> ep0, Span<int> ep1)
    {
        int v0 = v[0], v1 = v[1], v2 = v[2], v3 = v[3], v4 = v[4], v5 = v[5];

        var modeval = ((v1 & 0x80) >> 7) | (((v2 & 0x80) >> 7) << 1) | (((v3 & 0x80) >> 7) << 2);
        var majcomp = ((v4 & 0x80) >> 7) | (((v5 & 0x80) >> 7) << 1);

        if (majcomp == 3)
        {
            Set(ep0, v0 << 8, v2 << 8, (v4 & 0x7F) << 9);
            Set(ep1, v1 << 8, v3 << 8, (v5 & 0x7F) << 9);
            return;
        }

        var a = v0 | ((v1 & 0x40) << 2);
        var b0 = v2 & 0x3F;
        var b1 = v3 & 0x3F;
        var c = v1 & 0x3F;
        var d0 = v4 & 0x7F;
        var d1 = v5 & 0x7F;

        ReadOnlySpan<int> dbitsTable = [7, 6, 7, 6, 5, 6, 5, 6];
        var dbits = dbitsTable[modeval];

        var bit0 = (v2 >> 6) & 1;
        var bit1 = (v3 >> 6) & 1;
        var bit2 = (v4 >> 6) & 1;
        var bit3 = (v5 >> 6) & 1;
        var bit4 = (v4 >> 5) & 1;
        var bit5 = (v5 >> 5) & 1;

        var ohmod = 1 << modeval;
        if ((ohmod & 0xA4) != 0) a |= bit0 << 9;
        if ((ohmod & 0x8) != 0) a |= bit2 << 9;
        if ((ohmod & 0x50) != 0) a |= bit4 << 9;

        if ((ohmod & 0x50) != 0) a |= bit5 << 10;
        if ((ohmod & 0xA0) != 0) a |= bit1 << 10;

        if ((ohmod & 0xC0) != 0) a |= bit2 << 11;

        if ((ohmod & 0x4) != 0) c |= bit1 << 6;
        if ((ohmod & 0xE8) != 0) c |= bit3 << 6;

        if ((ohmod & 0x20) != 0) c |= bit2 << 7;

        if ((ohmod & 0x5B) != 0)
        {
            b0 |= bit0 << 6;
            b1 |= bit1 << 6;
        }

        if ((ohmod & 0x12) != 0)
        {
            b0 |= bit2 << 7;
            b1 |= bit3 << 7;
        }

        if ((ohmod & 0xAF) != 0)
        {
            d0 |= bit4 << 5;
            d1 |= bit5 << 5;
        }

        if ((ohmod & 0x5) != 0)
        {
            d0 |= bit2 << 6;
            d1 |= bit3 << 6;
        }

        // Sign-extend d0 and d1 from dbits.
        var sxShift = 32 - dbits;
        d0 = (d0 << sxShift) >> sxShift;
        d1 = (d1 << sxShift) >> sxShift;

        // Expand to 12 bits.
        var valShift = (modeval >> 1) ^ 3;
        a <<= valShift;
        b0 <<= valShift;
        b1 <<= valShift;
        c <<= valShift;
        d0 <<= valShift;
        d1 <<= valShift;

        var red1 = Math.Clamp(a, 0, 4095);
        var green1 = Math.Clamp(a - b0, 0, 4095);
        var blue1 = Math.Clamp(a - b1, 0, 4095);
        var red0 = Math.Clamp(a - c, 0, 4095);
        var green0 = Math.Clamp(a - b0 - c - d0, 0, 4095);
        var blue0 = Math.Clamp(a - b1 - c - d1, 0, 4095);

        if (majcomp == 1)
        {
            (red0, green0) = (green0, red0);
            (red1, green1) = (green1, red1);
        }
        else if (majcomp == 2)
        {
            (red0, blue0) = (blue0, red0);
            (red1, blue1) = (blue1, red1);
        }

        Set(ep0, red0 << 4, green0 << 4, blue0 << 4);
        Set(ep1, red1 << 4, green1 << 4, blue1 << 4);
    }

    // The HDR alpha of mode 15.
    public static void Alpha(int v6, int v7, out int alpha0, out int alpha1)
    {
        var selector = ((v6 >> 7) & 1) | ((v7 >> 6) & 2);
        v6 &= 0x7F;
        v7 &= 0x7F;

        if (selector == 3)
        {
            alpha0 = v6 << 5;
            alpha1 = v7 << 5;
        }
        else
        {
            v6 |= (v7 << (selector + 1)) & 0x780;
            v7 &= 0x3F >> selector;
            v7 ^= 32 >> selector;
            v7 -= 32 >> selector;
            v6 <<= 4 - selector;
            v7 <<= 4 - selector;
            v7 = Math.Clamp(v7 + v6, 0, 0xFFF);

            alpha0 = v6;
            alpha1 = v7;
        }

        alpha0 <<= 4;
        alpha1 <<= 4;
    }

    /// <summary>A 16-bit logarithmic value as a half float (astcenc <c>lns_to_sf16</c>).</summary>
    public static float LnsToFloat(int p)
    {
        var mc = p & 0x7FF;
        var ec = p >> 11;

        var mt = mc < 512 ? mc * 3
            : mc < 1536 ? (mc * 4) - 512
            : (mc * 5) - 2048;

        var bits = Math.Min((ec << 10) | (mt >> 3), 0x7BFF);
        return (float)BitConverter.UInt16BitsToHalf((ushort)bits);
    }

    /// <summary>A 16-bit unorm value as a half float (astcenc <c>unorm16_to_sf16</c>).</summary>
    public static float Unorm16ToFloat(int p)
    {
        int bits;
        if (p == 0xFFFF)
        {
            bits = 0x3C00;
        }
        else if (p < 4)
        {
            bits = p << 8;
        }
        else
        {
            var lz = BitOperations.LeadingZeroCount((uint)p) - 16;
            var m = (p << (lz + 1)) & 0xFFFF;
            bits = (m >> 6) | ((14 - lz) << 10);
        }

        return (float)BitConverter.UInt16BitsToHalf((ushort)bits);
    }

    private static void Set(Span<int> ep, int r, int g, int b)
    {
        ep[0] = r;
        ep[1] = g;
        ep[2] = b;
    }
}