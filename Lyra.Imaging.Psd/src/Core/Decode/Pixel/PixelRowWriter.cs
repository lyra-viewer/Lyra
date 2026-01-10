using System.Diagnostics;
using Lyra.Imaging.Psd.Core.Decode.Color.ColorCalibration;

namespace Lyra.Imaging.Psd.Core.Decode.Pixel;

public static class PixelRowWriter
{
    public readonly record struct CmykHeuristicTuning(
        byte[] GammaLut,
        bool EnableNeutralBalance,
        int NeutralThreshold,
        int NeutralRAdd,
        int NeutralGAdd,
        int NeutralBAdd
    );

    public static void WriteRgbRow(
        Span<byte> dstRow,
        PixelFormat dstPixelFormat,
        AlphaType alphaType,
        ReadOnlySpan<byte> rRow,
        ReadOnlySpan<byte> gRow,
        ReadOnlySpan<byte> bRow,
        ReadOnlySpan<byte> aRow,
        bool hasAlpha,
        bool useCalibration,
        RgbLuts cal)
    {
        Debug.Assert(rRow.Length == gRow.Length && rRow.Length == bRow.Length, "RGB plane rows must have equal length.");
        Debug.Assert(!hasAlpha || aRow.Length >= rRow.Length, "Alpha row must be at least as long as color rows when hasAlpha=true.");
        Debug.Assert(dstRow.Length >= rRow.Length * 4, "Destination row span too small.");

        if (useCalibration)
        {
            if (cal.R is null || cal.G is null || cal.B is null)
                throw new ArgumentNullException(nameof(cal), "Calibration LUTs are required when useCalibration=true.");

            if (cal.R.Length < 256 || cal.G.Length < 256 || cal.B.Length < 256)
                throw new ArgumentException("Calibration LUTs must have length >= 256.", nameof(cal));
        }

        switch (dstPixelFormat)
        {
            case PixelFormat.Bgra8888:
                WriteRgbBgra(dstRow, alphaType, rRow, gRow, bRow, aRow, hasAlpha, useCalibration, cal);
                return;
            case PixelFormat.Rgba8888:
                WriteRgbRgba(dstRow, alphaType, rRow, gRow, bRow, aRow, hasAlpha, useCalibration, cal);
                return;
            default:
                throw new NotSupportedException($"Unsupported output PixelFormat: {dstPixelFormat}");
        }
    }

    public static void WriteCmykRow(
        Span<byte> dstRow,
        PixelFormat dstPixelFormat,
        AlphaType alphaType,
        ReadOnlySpan<byte> cRow,
        ReadOnlySpan<byte> mRow,
        ReadOnlySpan<byte> yRow,
        ReadOnlySpan<byte> kRow,
        ReadOnlySpan<byte> aRow,
        bool hasAlpha,
        bool useHeuristicTuning,
        CmykHeuristicTuning tuning,
        RgbLuts cal)
    {
        Debug.Assert(cRow.Length == mRow.Length && cRow.Length == yRow.Length && cRow.Length == kRow.Length, "CMYK plane rows must have equal length.");
        Debug.Assert(!hasAlpha || aRow.Length >= cRow.Length, "Alpha row must be at least as long as color rows when hasAlpha=true.");
        Debug.Assert(dstRow.Length >= cRow.Length * 4, "Destination row span too small.");

        if (useHeuristicTuning)
        {
            if (tuning.GammaLut is null)
                throw new ArgumentNullException(nameof(tuning), "Tuning GammaLut is required when useHeuristicTuning=true.");

            if (tuning.GammaLut.Length < 256)
                throw new ArgumentException("Tuning GammaLut must have length >= 256.", nameof(tuning));
        }
        else
        {
            // calibration path (implied by !useHeuristicTuning)
            if (cal.R is null || cal.G is null || cal.B is null)
                throw new ArgumentNullException(nameof(cal), "Calibration LUTs are required when useHeuristicTuning=false.");

            if (cal.R.Length < 256 || cal.G.Length < 256 || cal.B.Length < 256)
                throw new ArgumentException("Calibration LUTs must have length >= 256.", nameof(cal));
        }
        
        switch (dstPixelFormat)
        {
            case PixelFormat.Bgra8888:
                WriteCmykBgra(dstRow, alphaType, cRow, mRow, yRow, kRow, aRow, hasAlpha, useHeuristicTuning, tuning, cal);
                return;
            case PixelFormat.Rgba8888:
                WriteCmykRgba(dstRow, alphaType, cRow, mRow, yRow, kRow, aRow, hasAlpha, useHeuristicTuning, tuning, cal);
                return;
            default:
                throw new NotSupportedException($"Unsupported output PixelFormat: {dstPixelFormat}");
        }
    }

    private static void WriteRgbBgra(
        Span<byte> dstRow,
        AlphaType alphaType,
        ReadOnlySpan<byte> rRow,
        ReadOnlySpan<byte> gRow,
        ReadOnlySpan<byte> bRow,
        ReadOnlySpan<byte> aRow,
        bool hasAlpha,
        bool useCalibration,
        RgbLuts cal)
    {
        var di = 0;
        if (!hasAlpha)
        {
            for (var x = 0; x < rRow.Length; x++)
            {
                var rr = rRow[x];
                var gg = gRow[x];
                var bb = bRow[x];

                if (useCalibration)
                {
                    rr = cal.R[rr];
                    gg = cal.G[gg];
                    bb = cal.B[bb];
                }

                dstRow[di + 0] = bb;
                dstRow[di + 1] = gg;
                dstRow[di + 2] = rr;
                dstRow[di + 3] = 255;
                di += 4;
            }

            return;
        }

        if (alphaType == AlphaType.Straight)
        {
            for (var x = 0; x < rRow.Length; x++)
            {
                var rr = rRow[x];
                var gg = gRow[x];
                var bb = bRow[x];
                var aa = aRow[x];

                if (useCalibration)
                {
                    rr = cal.R[rr];
                    gg = cal.G[gg];
                    bb = cal.B[bb];
                }

                dstRow[di + 0] = bb;
                dstRow[di + 1] = gg;
                dstRow[di + 2] = rr;
                dstRow[di + 3] = aa;
                di += 4;
            }
        }
        else
        {
            for (var x = 0; x < rRow.Length; x++)
            {
                var rr = rRow[x];
                var gg = gRow[x];
                var bb = bRow[x];
                var aa = aRow[x];

                if (useCalibration)
                {
                    rr = cal.R[rr];
                    gg = cal.G[gg];
                    bb = cal.B[bb];
                }

                dstRow[di + 0] = Premultiply(bb, aa);
                dstRow[di + 1] = Premultiply(gg, aa);
                dstRow[di + 2] = Premultiply(rr, aa);
                dstRow[di + 3] = aa;
                di += 4;
            }
        }
    }

    private static void WriteRgbRgba(
        Span<byte> dstRow,
        AlphaType alphaType,
        ReadOnlySpan<byte> rRow,
        ReadOnlySpan<byte> gRow,
        ReadOnlySpan<byte> bRow,
        ReadOnlySpan<byte> aRow,
        bool hasAlpha,
        bool useCalibration,
        RgbLuts cal)
    {
        var di = 0;
        if (!hasAlpha)
        {
            for (var x = 0; x < rRow.Length; x++)
            {
                var rr = rRow[x];
                var gg = gRow[x];
                var bb = bRow[x];

                if (useCalibration)
                {
                    rr = cal.R[rr];
                    gg = cal.G[gg];
                    bb = cal.B[bb];
                }

                dstRow[di + 0] = rr;
                dstRow[di + 1] = gg;
                dstRow[di + 2] = bb;
                dstRow[di + 3] = 255;
                di += 4;
            }

            return;
        }

        if (alphaType == AlphaType.Straight)
        {
            for (var x = 0; x < rRow.Length; x++)
            {
                var rr = rRow[x];
                var gg = gRow[x];
                var bb = bRow[x];
                var aa = aRow[x];

                if (useCalibration)
                {
                    rr = cal.R[rr];
                    gg = cal.G[gg];
                    bb = cal.B[bb];
                }

                dstRow[di + 0] = rr;
                dstRow[di + 1] = gg;
                dstRow[di + 2] = bb;
                dstRow[di + 3] = aa;
                di += 4;
            }
        }
        else
        {
            for (var x = 0; x < rRow.Length; x++)
            {
                var rr = rRow[x];
                var gg = gRow[x];
                var bb = bRow[x];
                var aa = aRow[x];

                if (useCalibration)
                {
                    rr = cal.R[rr];
                    gg = cal.G[gg];
                    bb = cal.B[bb];
                }

                dstRow[di + 0] = Premultiply(rr, aa);
                dstRow[di + 1] = Premultiply(gg, aa);
                dstRow[di + 2] = Premultiply(bb, aa);
                dstRow[di + 3] = aa;
                di += 4;
            }
        }
    }

    private static void WriteCmykBgra(
        Span<byte> dstRow,
        AlphaType alphaType,
        ReadOnlySpan<byte> cRow,
        ReadOnlySpan<byte> mRow,
        ReadOnlySpan<byte> yRow,
        ReadOnlySpan<byte> kRow,
        ReadOnlySpan<byte> aRow,
        bool hasAlpha,
        bool useHeuristicTuning,
        CmykHeuristicTuning tuning,
        RgbLuts cal)
    {
        var di = 0;
        if (!hasAlpha)
        {
            for (var x = 0; x < cRow.Length; x++)
            {
                ConvertCmykApprox(cRow[x], mRow[x], yRow[x], kRow[x], out var r, out var g, out var b);

                if (useHeuristicTuning)
                    ApplyCmykHeuristicTuning(ref r, ref g, ref b, tuning);
                else
                {
                    r = cal.R[r];
                    g = cal.G[g];
                    b = cal.B[b];
                }

                dstRow[di + 0] = b;
                dstRow[di + 1] = g;
                dstRow[di + 2] = r;
                dstRow[di + 3] = 255;
                di += 4;
            }

            return;
        }

        if (alphaType == AlphaType.Straight)
        {
            for (var x = 0; x < cRow.Length; x++)
            {
                ConvertCmykApprox(cRow[x], mRow[x], yRow[x], kRow[x], out var r, out var g, out var b);
                var a = aRow[x];

                if (useHeuristicTuning)
                    ApplyCmykHeuristicTuning(ref r, ref g, ref b, tuning);
                else
                {
                    r = cal.R[r];
                    g = cal.G[g];
                    b = cal.B[b];
                }

                dstRow[di + 0] = b;
                dstRow[di + 1] = g;
                dstRow[di + 2] = r;
                dstRow[di + 3] = a;
                di += 4;
            }
        }
        else
        {
            for (var x = 0; x < cRow.Length; x++)
            {
                ConvertCmykApprox(cRow[x], mRow[x], yRow[x], kRow[x], out var r, out var g, out var b);
                var a = aRow[x];

                if (useHeuristicTuning)
                    ApplyCmykHeuristicTuning(ref r, ref g, ref b, tuning);
                else
                {
                    r = cal.R[r];
                    g = cal.G[g];
                    b = cal.B[b];
                }

                dstRow[di + 0] = Premultiply(b, a);
                dstRow[di + 1] = Premultiply(g, a);
                dstRow[di + 2] = Premultiply(r, a);
                dstRow[di + 3] = a;
                di += 4;
            }
        }
    }

    private static void WriteCmykRgba(
        Span<byte> dstRow,
        AlphaType alphaType,
        ReadOnlySpan<byte> cRow,
        ReadOnlySpan<byte> mRow,
        ReadOnlySpan<byte> yRow,
        ReadOnlySpan<byte> kRow,
        ReadOnlySpan<byte> aRow,
        bool hasAlpha,
        bool useHeuristicTuning,
        CmykHeuristicTuning tuning,
        RgbLuts cal)
    {
        var di = 0;
        if (!hasAlpha)
        {
            for (var x = 0; x < cRow.Length; x++)
            {
                ConvertCmykApprox(cRow[x], mRow[x], yRow[x], kRow[x], out var r, out var g, out var b);

                if (useHeuristicTuning)
                    ApplyCmykHeuristicTuning(ref r, ref g, ref b, tuning);
                else
                {
                    r = cal.R[r];
                    g = cal.G[g];
                    b = cal.B[b];
                }

                dstRow[di + 0] = r;
                dstRow[di + 1] = g;
                dstRow[di + 2] = b;
                dstRow[di + 3] = 255;
                di += 4;
            }

            return;
        }

        if (alphaType == AlphaType.Straight)
        {
            for (var x = 0; x < cRow.Length; x++)
            {
                ConvertCmykApprox(cRow[x], mRow[x], yRow[x], kRow[x], out var r, out var g, out var b);
                var a = aRow[x];

                if (useHeuristicTuning)
                    ApplyCmykHeuristicTuning(ref r, ref g, ref b, tuning);
                else
                {
                    r = cal.R[r];
                    g = cal.G[g];
                    b = cal.B[b];
                }

                dstRow[di + 0] = r;
                dstRow[di + 1] = g;
                dstRow[di + 2] = b;
                dstRow[di + 3] = a;
                di += 4;
            }
        }
        else
        {
            for (var x = 0; x < cRow.Length; x++)
            {
                ConvertCmykApprox(cRow[x], mRow[x], yRow[x], kRow[x], out var r, out var g, out var b);
                var a = aRow[x];

                if (useHeuristicTuning)
                    ApplyCmykHeuristicTuning(ref r, ref g, ref b, tuning);
                else
                {
                    r = cal.R[r];
                    g = cal.G[g];
                    b = cal.B[b];
                }

                dstRow[di + 0] = Premultiply(r, a);
                dstRow[di + 1] = Premultiply(g, a);
                dstRow[di + 2] = Premultiply(b, a);
                dstRow[di + 3] = a;
                di += 4;
            }
        }
    }

    private static void ApplyCmykHeuristicTuning(ref byte r, ref byte g, ref byte b, CmykHeuristicTuning tuning)
    {
        r = tuning.GammaLut[r];
        g = tuning.GammaLut[g];
        b = tuning.GammaLut[b];

        if (tuning.EnableNeutralBalance)
            ApplyNeutralBalance(ref r, ref g, ref b, tuning.NeutralThreshold, tuning.NeutralRAdd, tuning.NeutralGAdd, tuning.NeutralBAdd);
    }

    private static void ApplyNeutralBalance(ref byte r, ref byte g, ref byte b, int threshold, int rAdd, int gAdd, int bAdd)
    {
        int max = r;
        if (g > max) max = g;
        if (b > max) max = b;

        int min = r;
        if (g < min) min = g;
        if (b < min) min = b;

        if (max - min > threshold)
            return;

        r = ClampToByte(r + rAdd);
        g = ClampToByte(g + gAdd);
        b = ClampToByte(b + bAdd);
    }

    private static void ConvertCmykApprox(byte c0, byte m0, byte y0, byte k0, out byte r, out byte g, out byte b)
    {
        var c = (byte)(255 - c0);
        var m = (byte)(255 - m0);
        var y = (byte)(255 - y0);
        var k = (byte)(255 - k0);

        r = ToRgbComponent(c, k);
        g = ToRgbComponent(m, k);
        b = ToRgbComponent(y, k);
    }

    private static byte ToRgbComponent(byte ink, byte k)
    {
        var c = ink / 255f;
        var kk = k / 255f;
        var v = 255f * (1f - c) * (1f - kk);
        var iv = (int)(v + 0.5f);
        if (iv < 0) iv = 0;
        if (iv > 255) iv = 255;
        return (byte)iv;
    }

    private static byte Premultiply(byte c, byte a) => (byte)((c * a + 127) / 255);

    private static byte ClampToByte(int v) => v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)v;
}