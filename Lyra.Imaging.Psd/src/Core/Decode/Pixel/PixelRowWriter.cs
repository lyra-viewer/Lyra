using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lyra.Imaging.Psd.Core.Decode.ColorCalibration.Cmyk;
using Lyra.Imaging.Psd.Core.Decode.ColorCalibration.Rgb;

namespace Lyra.Imaging.Psd.Core.Decode.Pixel;

// ============================================================================
//  PERFORMANCE CRITICAL – PIXEL ROW WRITER
// ----------------------------------------------------------------------------
//  Converts decoded plane data into final RGBA/BGRA surface rows.
//  This is one of the hottest loops in the entire PSD decode pipeline.
//
//  Hot Path Characteristics:
//    - Executes once per output pixel.
//    - Responsible for:
//      - Channel packing
//      - Alpha handling
//      - Premultiplication
//      - ICC calibration LUT application
//      - CMYK approximation / tuning
//    - Must remain allocation-free.
//    - Must minimize branching inside inner loops.
//    - Must not mix heuristic tuning and ICC LUT paths incorrectly.
//
//  Critical Invariants:
//    - Calibration LUTs are keyed by baseline approximation output.
//    - LUT application MUST occur on baseline RGB values.
//    - Heuristic tuning and ICC calibration are mutually exclusive paths.
//    - Indexed palette layout must match PSD Color Mode Data interpretation.
//
//  PERFORMANCE CONTRACT:
//    Changes here directly impact large PSB decode time.
//    Any modification must be benchmarked against large (≥3GB) PSB files.
//    Avoid:
//      - Division in inner loops
//      - Extra bounds checks
//      - Additional Span slicing
//      - Refactors that reintroduce nested branching
//
//  Verified against:
//    - Reference color correctness tests
//    - CMYK polarity validation
//    - ICC calibration LUT validation
//    - 3GB PSB benchmark
// ============================================================================
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

    #region RGB

    public static void WriteRgbRow(
        Span<byte> dstRow,
        PixelFormat dstPixelFormat,
        AlphaType alphaType,
        ReadOnlySpan<byte> rRow, ReadOnlySpan<byte> gRow, ReadOnlySpan<byte> bRow, ReadOnlySpan<byte> aRow,
        bool hasAlpha,
        bool useCalibration,
        RgbLuts cal)
    {
        Debug.Assert(rRow.Length == gRow.Length && rRow.Length == bRow.Length, "RGB plane rows must have equal length.");
        Debug.Assert(!hasAlpha || aRow.Length >= rRow.Length, "Alpha row must be at least as long as color rows when hasAlpha=true.");
        Debug.Assert(dstRow.Length >= rRow.Length * 4, "Destination row span too small.");

        var isBgra = dstPixelFormat == PixelFormat.Bgra8888;
        var isRgba = dstPixelFormat == PixelFormat.Rgba8888;
        if (!isBgra && !isRgba)
            throw new NotSupportedException($"Unsupported PixelFormat: {dstPixelFormat}");

        var width = rRow.Length;

        // Resolve LUTs up front
        byte[]? lutR = null, lutG = null, lutB = null;
        if (useCalibration)
        {
            lutR = cal.R;
            lutG = cal.G;
            lutB = cal.B;

            if (lutR is null || lutG is null || lutB is null)
                throw new ArgumentNullException(nameof(cal), "Calibration LUTs are required when useCalibration=true.");

            if (lutR.Length < 256 || lutG.Length < 256 || lutB.Length < 256)
                throw new ArgumentException("Calibration LUTs must have length >= 256.", nameof(cal));
        }

        bool premul = hasAlpha && alphaType == AlphaType.Premultiplied;

        if (!hasAlpha)
        {
            if (useCalibration)
                WriteRgb_NoAlpha_Lut(dstRow, isBgra, rRow, gRow, bRow, lutR!, lutG!, lutB!, width);
            else
                WriteRgb_NoAlpha(dstRow, isBgra, rRow, gRow, bRow, width);

            return;
        }

        if (!premul)
        {
            if (useCalibration)
                WriteRgb_AlphaStraight_Lut(dstRow, isBgra, rRow, gRow, bRow, aRow, lutR!, lutG!, lutB!, width);
            else
                WriteRgb_AlphaStraight(dstRow, isBgra, rRow, gRow, bRow, aRow, width);

            return;
        }

        // premul
        if (useCalibration)
            WriteRgb_AlphaPremul_Lut(dstRow, isBgra, rRow, gRow, bRow, aRow, lutR!, lutG!, lutB!, width);
        else
            WriteRgb_AlphaPremul(dstRow, isBgra, rRow, gRow, bRow, aRow, width);
    }

    private static void WriteRgb_NoAlpha(
        Span<byte> dst, bool isBgra,
        ReadOnlySpan<byte> r, ReadOnlySpan<byte> g, ReadOnlySpan<byte> b,
        int width)
    {
        for (int i = 0, di = 0; i < width; i++, di += 4)
        {
            byte rr = r[i], gg = g[i], bb = b[i];

            if (isBgra)
            {
                dst[di + 0] = bb;
                dst[di + 1] = gg;
                dst[di + 2] = rr;
                dst[di + 3] = 255;
            }
            else
            {
                dst[di + 0] = rr;
                dst[di + 1] = gg;
                dst[di + 2] = bb;
                dst[di + 3] = 255;
            }
        }
    }

    private static void WriteRgb_NoAlpha_Lut(
        Span<byte> dst, bool isBgra,
        ReadOnlySpan<byte> r, ReadOnlySpan<byte> g, ReadOnlySpan<byte> b,
        byte[] lutR, byte[] lutG, byte[] lutB,
        int width)
    {
        for (int i = 0, di = 0; i < width; i++, di += 4)
        {
            byte rr = lutR[r[i]];
            byte gg = lutG[g[i]];
            byte bb = lutB[b[i]];

            if (isBgra)
            {
                dst[di + 0] = bb;
                dst[di + 1] = gg;
                dst[di + 2] = rr;
                dst[di + 3] = 255;
            }
            else
            {
                dst[di + 0] = rr;
                dst[di + 1] = gg;
                dst[di + 2] = bb;
                dst[di + 3] = 255;
            }
        }
    }

    private static void WriteRgb_AlphaStraight(
        Span<byte> dst, bool isBgra,
        ReadOnlySpan<byte> r, ReadOnlySpan<byte> g, ReadOnlySpan<byte> b, ReadOnlySpan<byte> a,
        int width)
    {
        for (int i = 0, di = 0; i < width; i++, di += 4)
        {
            byte rr = r[i], gg = g[i], bb = b[i], aa = a[i];

            if (isBgra)
            {
                dst[di + 0] = bb;
                dst[di + 1] = gg;
                dst[di + 2] = rr;
                dst[di + 3] = aa;
            }
            else
            {
                dst[di + 0] = rr;
                dst[di + 1] = gg;
                dst[di + 2] = bb;
                dst[di + 3] = aa;
            }
        }
    }

    private static void WriteRgb_AlphaStraight_Lut(
        Span<byte> dst, bool isBgra,
        ReadOnlySpan<byte> r, ReadOnlySpan<byte> g, ReadOnlySpan<byte> b, ReadOnlySpan<byte> a,
        byte[] lutR, byte[] lutG, byte[] lutB,
        int width)
    {
        for (int i = 0, di = 0; i < width; i++, di += 4)
        {
            byte rr = lutR[r[i]];
            byte gg = lutG[g[i]];
            byte bb = lutB[b[i]];
            byte aa = a[i];

            if (isBgra)
            {
                dst[di + 0] = bb;
                dst[di + 1] = gg;
                dst[di + 2] = rr;
                dst[di + 3] = aa;
            }
            else
            {
                dst[di + 0] = rr;
                dst[di + 1] = gg;
                dst[di + 2] = bb;
                dst[di + 3] = aa;
            }
        }
    }

    private static void WriteRgb_AlphaPremul(
        Span<byte> dst, bool isBgra,
        ReadOnlySpan<byte> r, ReadOnlySpan<byte> g, ReadOnlySpan<byte> b, ReadOnlySpan<byte> a,
        int width)
    {
        for (int i = 0, di = 0; i < width; i++, di += 4)
        {
            byte rr = r[i], gg = g[i], bb = b[i], aa = a[i];

            if (aa < 255)
            {
                rr = PremultiplyFast(rr, aa);
                gg = PremultiplyFast(gg, aa);
                bb = PremultiplyFast(bb, aa);
            }

            if (isBgra)
            {
                dst[di + 0] = bb;
                dst[di + 1] = gg;
                dst[di + 2] = rr;
                dst[di + 3] = aa;
            }
            else
            {
                dst[di + 0] = rr;
                dst[di + 1] = gg;
                dst[di + 2] = bb;
                dst[di + 3] = aa;
            }
        }
    }

    private static void WriteRgb_AlphaPremul_Lut(
        Span<byte> dst, bool isBgra,
        ReadOnlySpan<byte> r, ReadOnlySpan<byte> g, ReadOnlySpan<byte> b,
        ReadOnlySpan<byte> a,
        byte[] lutR, byte[] lutG, byte[] lutB,
        int width)
    {
        for (int i = 0, di = 0; i < width; i++, di += 4)
        {
            byte rr = lutR[r[i]];
            byte gg = lutG[g[i]];
            byte bb = lutB[b[i]];
            byte aa = a[i];

            if (aa < 255)
            {
                rr = PremultiplyFast(rr, aa);
                gg = PremultiplyFast(gg, aa);
                bb = PremultiplyFast(bb, aa);
            }

            if (isBgra)
            {
                dst[di + 0] = bb;
                dst[di + 1] = gg;
                dst[di + 2] = rr;
                dst[di + 3] = aa;
            }
            else
            {
                dst[di + 0] = rr;
                dst[di + 1] = gg;
                dst[di + 2] = bb;
                dst[di + 3] = aa;
            }
        }
    }

    #endregion

    #region CMYK

    private static readonly byte[] PaperMulLut = BuildPaperMulLut();

    private static byte[] BuildPaperMulLut()
    {
        var t = new byte[256 * 256];
        for (int a = 0; a < 256; a++)
        for (int b = 0; b < 256; b++)
        {
            int temp = a * b + 128;
            t[(a << 8) | b] = (byte)((temp + (temp >> 8)) >> 8);
        }

        return t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte PaperMul(byte a, byte b) => PaperMulLut[(a << 8) | b];

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
        in CmykHeuristicTuning tuning,
        RgbLuts cal)
    {
        Debug.Assert(cRow.Length == mRow.Length && cRow.Length == yRow.Length && cRow.Length == kRow.Length, "CMYK plane rows must have equal length.");
        Debug.Assert(!hasAlpha || aRow.Length >= cRow.Length, "Alpha row must be at least as long as color rows when hasAlpha=true.");
        Debug.Assert(dstRow.Length >= cRow.Length * 4, "Destination row span too small.");

        var isBgra = dstPixelFormat == PixelFormat.Bgra8888;
        var isRgba = dstPixelFormat == PixelFormat.Rgba8888;
        if (!isBgra && !isRgba)
            throw new NotSupportedException($"Unsupported PixelFormat: {dstPixelFormat}");

        var width = cRow.Length;

        if (useHeuristicTuning)
        {
            if (tuning.GammaLut is null || tuning.GammaLut.Length < 256)
                throw new ArgumentException("Tuning GammaLut must have length >= 256.", nameof(tuning));

            bool premul = hasAlpha && alphaType == AlphaType.Premultiplied;

            if (!hasAlpha)
            {
                WriteCmyk_NoAlpha_Tuning(dstRow, isBgra, cRow, mRow, yRow, kRow, in tuning, width);
                return;
            }

            if (!premul)
            {
                WriteCmyk_AlphaStraight_Tuning(dstRow, isBgra, cRow, mRow, yRow, kRow, aRow, in tuning, width);
                return;
            }

            WriteCmyk_AlphaPremul_Tuning(dstRow, isBgra, cRow, mRow, yRow, kRow, aRow, in tuning, width);
        }
        else
        {
            var lutR = cal.R;
            var lutG = cal.G;
            var lutB = cal.B;

            if (lutR is null || lutG is null || lutB is null)
                throw new ArgumentNullException(nameof(cal), "Calibration LUTs are required when useHeuristicTuning=false.");

            if (lutR.Length < 256 || lutG.Length < 256 || lutB.Length < 256)
                throw new ArgumentException("Calibration LUTs must have length >= 256.", nameof(cal));

            bool premul = hasAlpha && alphaType == AlphaType.Premultiplied;

            if (!hasAlpha)
            {
                WriteCmyk_NoAlpha_Lut(dstRow, isBgra, cRow, mRow, yRow, kRow, lutR, lutG, lutB, width);
                return;
            }

            if (!premul)
            {
                WriteCmyk_AlphaStraight_Lut(dstRow, isBgra, cRow, mRow, yRow, kRow, aRow, lutR, lutG, lutB, width);
                return;
            }

            WriteCmyk_AlphaPremul_Lut(dstRow, isBgra, cRow, mRow, yRow, kRow, aRow, lutR, lutG, lutB, width);
        }
    }

    private static void WriteCmyk_NoAlpha_Lut(
        Span<byte> dst, bool isBgra,
        ReadOnlySpan<byte> c, ReadOnlySpan<byte> m, ReadOnlySpan<byte> y, ReadOnlySpan<byte> k,
        byte[] lutR, byte[] lutG, byte[] lutB,
        int width)
    {
        for (int i = 0, di = 0; i < width; i++, di += 4)
        {
            byte kk = k[i];
            byte r0 = PaperMul(c[i], kk);
            byte g0 = PaperMul(m[i], kk);
            byte b0 = PaperMul(y[i], kk);

            byte r = lutR[r0];
            byte g = lutG[g0];
            byte b = lutB[b0];

            if (isBgra)
            {
                dst[di + 0] = b;
                dst[di + 1] = g;
                dst[di + 2] = r;
                dst[di + 3] = 255;
            }
            else
            {
                dst[di + 0] = r;
                dst[di + 1] = g;
                dst[di + 2] = b;
                dst[di + 3] = 255;
            }
        }
    }

    private static void WriteCmyk_AlphaStraight_Lut(
        Span<byte> dst, bool isBgra,
        ReadOnlySpan<byte> c, ReadOnlySpan<byte> m, ReadOnlySpan<byte> y, ReadOnlySpan<byte> k, ReadOnlySpan<byte> a,
        byte[] lutR, byte[] lutG, byte[] lutB,
        int width)
    {
        for (int i = 0, di = 0; i < width; i++, di += 4)
        {
            byte kk = k[i];
            byte r0 = PaperMul(c[i], kk);
            byte g0 = PaperMul(m[i], kk);
            byte b0 = PaperMul(y[i], kk);

            byte r = lutR[r0];
            byte g = lutG[g0];
            byte b = lutB[b0];

            byte aa = a[i];

            if (isBgra)
            {
                dst[di + 0] = b;
                dst[di + 1] = g;
                dst[di + 2] = r;
                dst[di + 3] = aa;
            }
            else
            {
                dst[di + 0] = r;
                dst[di + 1] = g;
                dst[di + 2] = b;
                dst[di + 3] = aa;
            }
        }
    }

    private static void WriteCmyk_AlphaPremul_Lut(
        Span<byte> dst, bool isBgra,
        ReadOnlySpan<byte> c, ReadOnlySpan<byte> m, ReadOnlySpan<byte> y, ReadOnlySpan<byte> k, ReadOnlySpan<byte> a,
        byte[] lutR, byte[] lutG, byte[] lutB,
        int width)
    {
        for (int i = 0, di = 0; i < width; i++, di += 4)
        {
            byte kk = k[i];
            byte r0 = PaperMul(c[i], kk);
            byte g0 = PaperMul(m[i], kk);
            byte b0 = PaperMul(y[i], kk);

            byte r = lutR[r0];
            byte g = lutG[g0];
            byte b = lutB[b0];

            byte aa = a[i];
            if (aa < 255)
            {
                r = PremultiplyFast(r, aa);
                g = PremultiplyFast(g, aa);
                b = PremultiplyFast(b, aa);
            }

            if (isBgra)
            {
                dst[di + 0] = b;
                dst[di + 1] = g;
                dst[di + 2] = r;
                dst[di + 3] = aa;
            }
            else
            {
                dst[di + 0] = r;
                dst[di + 1] = g;
                dst[di + 2] = b;
                dst[di + 3] = aa;
            }
        }
    }

    private static void WriteCmyk_NoAlpha_Tuning(
        Span<byte> dst, bool isBgra,
        ReadOnlySpan<byte> c, ReadOnlySpan<byte> m, ReadOnlySpan<byte> y, ReadOnlySpan<byte> k,
        in CmykHeuristicTuning tuning,
        int width)
    {
        var gamma = tuning.GammaLut;
        var doNeutral = tuning.EnableNeutralBalance;
        var threshold = tuning.NeutralThreshold;
        var rAdd = tuning.NeutralRAdd;
        var gAdd = tuning.NeutralGAdd;
        var bAdd = tuning.NeutralBAdd;

        for (int i = 0, di = 0; i < width; i++, di += 4)
        {
            byte kk = k[i];
            byte r = gamma[PaperMul(c[i], kk)];
            byte g = gamma[PaperMul(m[i], kk)];
            byte b = gamma[PaperMul(y[i], kk)];

            if (doNeutral)
                ApplyNeutralBalanceFast(ref r, ref g, ref b, threshold, rAdd, gAdd, bAdd);

            if (isBgra)
            {
                dst[di + 0] = b;
                dst[di + 1] = g;
                dst[di + 2] = r;
                dst[di + 3] = 255;
            }
            else
            {
                dst[di + 0] = r;
                dst[di + 1] = g;
                dst[di + 2] = b;
                dst[di + 3] = 255;
            }
        }
    }

    private static void WriteCmyk_AlphaStraight_Tuning(
        Span<byte> dst, bool isBgra,
        ReadOnlySpan<byte> c, ReadOnlySpan<byte> m, ReadOnlySpan<byte> y, ReadOnlySpan<byte> k, ReadOnlySpan<byte> a,
        in CmykHeuristicTuning tuning,
        int width)
    {
        var gamma = tuning.GammaLut;
        var doNeutral = tuning.EnableNeutralBalance;
        var threshold = tuning.NeutralThreshold;
        var rAdd = tuning.NeutralRAdd;
        var gAdd = tuning.NeutralGAdd;
        var bAdd = tuning.NeutralBAdd;

        for (int i = 0, di = 0; i < width; i++, di += 4)
        {
            byte kk = k[i];
            byte r = gamma[PaperMul(c[i], kk)];
            byte g = gamma[PaperMul(m[i], kk)];
            byte b = gamma[PaperMul(y[i], kk)];

            if (doNeutral)
                ApplyNeutralBalanceFast(ref r, ref g, ref b, threshold, rAdd, gAdd, bAdd);

            byte aa = a[i];

            if (isBgra)
            {
                dst[di + 0] = b;
                dst[di + 1] = g;
                dst[di + 2] = r;
                dst[di + 3] = aa;
            }
            else
            {
                dst[di + 0] = r;
                dst[di + 1] = g;
                dst[di + 2] = b;
                dst[di + 3] = aa;
            }
        }
    }

    private static void WriteCmyk_AlphaPremul_Tuning(
        Span<byte> dst, bool isBgra,
        ReadOnlySpan<byte> c, ReadOnlySpan<byte> m, ReadOnlySpan<byte> y, ReadOnlySpan<byte> k, ReadOnlySpan<byte> a,
        in CmykHeuristicTuning tuning,
        int width)
    {
        var gamma = tuning.GammaLut;
        var doNeutral = tuning.EnableNeutralBalance;
        var threshold = tuning.NeutralThreshold;
        var rAdd = tuning.NeutralRAdd;
        var gAdd = tuning.NeutralGAdd;
        var bAdd = tuning.NeutralBAdd;

        for (int i = 0, di = 0; i < width; i++, di += 4)
        {
            byte kk = k[i];
            byte r = gamma[PaperMul(c[i], kk)];
            byte g = gamma[PaperMul(m[i], kk)];
            byte b = gamma[PaperMul(y[i], kk)];

            if (doNeutral)
                ApplyNeutralBalanceFast(ref r, ref g, ref b, threshold, rAdd, gAdd, bAdd);

            byte aa = a[i];
            if (aa < 255)
            {
                r = PremultiplyFast(r, aa);
                g = PremultiplyFast(g, aa);
                b = PremultiplyFast(b, aa);
            }

            if (isBgra)
            {
                dst[di + 0] = b;
                dst[di + 1] = g;
                dst[di + 2] = r;
                dst[di + 3] = aa;
            }
            else
            {
                dst[di + 0] = r;
                dst[di + 1] = g;
                dst[di + 2] = b;
                dst[di + 3] = aa;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyNeutralBalanceFast(ref byte r, ref byte g, ref byte b, int threshold, int rAdd, int gAdd, int bAdd)
    {
        int ri = r, gi = g, bi = b;

        int max = ri > gi ? ri : gi;
        if (bi > max) max = bi;

        int min = ri < gi ? ri : gi;
        if (bi < min) min = bi;

        if (max - min > threshold)
            return;

        r = ClampToByte(ri + rAdd);
        g = ClampToByte(gi + gAdd);
        b = ClampToByte(bi + bAdd);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CmykPaperToRgbBaseline(byte c0, byte m0, byte y0, byte k0, out byte r, out byte g, out byte b)
    {
        r = PaperMul(c0, k0);
        g = PaperMul(m0, k0);
        b = PaperMul(y0, k0);
    }

    // ========================================================================
    //  TETRAHEDRAL 4D INTERPOLATION — CMYK GRID LUT
    // ------------------------------------------------------------------------
    //  How it works:
    //    1. Locate the grid cell for each CMYK input.
    //    2. Compute fractional positions (fc, fm, fy, fk) within the cell.
    //    3. Sort fractional positions descending to select one of 24 simplices.
    //    4. Interpolate along 5 vertices (base corner + 4 progressive steps).
    // ========================================================================
    public static unsafe void WriteCmykRowGridLut(
        Span<byte> dstRow,
        PixelFormat dstPixelFormat,
        AlphaType alphaType,
        ReadOnlySpan<byte> cRow,
        ReadOnlySpan<byte> mRow,
        ReadOnlySpan<byte> yRow,
        ReadOnlySpan<byte> kRow,
        ReadOnlySpan<byte> aRow,
        bool hasAlpha,
        CmykGridLut lut,
        CmykGridLookupTables tables
    )
    {
        Debug.Assert(cRow.Length == mRow.Length && cRow.Length == yRow.Length && cRow.Length == kRow.Length, "CMYK plane rows must have equal length.");
        Debug.Assert(!hasAlpha || aRow.Length >= cRow.Length, "Alpha row must be at least as long as color rows when hasAlpha=true.");
        Debug.Assert(dstRow.Length >= cRow.Length * 4, "Destination row span too small.");

        var isBgra = dstPixelFormat == PixelFormat.Bgra8888;
        var isRgba = dstPixelFormat == PixelFormat.Rgba8888;
        if (!isBgra && !isRgba)
            throw new NotSupportedException($"Unsupported PixelFormat: {dstPixelFormat}");

        bool premul = hasAlpha && alphaType == AlphaType.Premultiplied;
        int width = cRow.Length;

        // Reference precomputed tables
        int[] tOffC0 = tables.OffC0, tOffC1 = tables.OffC1;
        int[] tOffM0 = tables.OffM0, tOffM1 = tables.OffM1;
        int[] tOffY0 = tables.OffY0, tOffY1 = tables.OffY1;
        int[] tOffK0 = tables.OffK0, tOffK1 = tables.OffK1;
        int[] tFrac = tables.Frac;

        // Per-row LRU hash cache (0 allocations, ~2KB stack)
        ulong* pCache = stackalloc ulong[256];
        new Span<ulong>(pCache, 256).Fill(0xFFFFFFFFFFFFFFFFul);

        fixed (byte* pSamples = lut.SamplesRgb)
        fixed (byte* pDst = dstRow)
        fixed (byte* pC = cRow, pM = mRow, pY = yRow, pK = kRow)
        fixed (byte* pA = aRow)
        {
            for (int i = 0, di = 0; i < width; i++, di += 4)
            {
                byte cv = pC[i];
                byte mv = pM[i];
                byte yv = pY[i];
                byte kv = pK[i];
                uint cmyk = (uint)(cv | (mv << 8) | (yv << 16) | (kv << 24));

                // Golden-ratio multiplicative hash for fast distribution
                uint hash = (cmyk * 0x9E3779B1) >> 24;
                ulong cacheLine = pCache[hash];

                byte outR, outG, outB;

                if ((uint)cacheLine == cmyk)
                {
                    uint rgb = (uint)(cacheLine >> 32);
                    outR = (byte)rgb;
                    outG = (byte)(rgb >> 8);
                    outB = (byte)(rgb >> 16);
                }
                else
                {
                    // Base corner offset in the samples array
                    int baseOff = tOffC0[cv] + tOffM0[mv] + tOffY0[yv] + tOffK0[kv];

                    // Step offsets - delta to move one grid step along each axis
                    int stepC = tOffC1[cv] - tOffC0[cv];
                    int stepM = tOffM1[mv] - tOffM0[mv];
                    int stepY = tOffY1[yv] - tOffY0[yv];
                    int stepK = tOffK1[kv] - tOffK0[kv];

                    // Fractional positions within the grid cell [0..255]
                    int fc = tFrac[cv];
                    int fm = tFrac[mv];
                    int fy = tFrac[yv];
                    int fk = tFrac[kv];

                    //  4-element sorting network (5 compare-swaps)
                    int f0 = fc, f1 = fm, f2 = fy, f3 = fk;
                    int s0 = stepC, s1 = stepM, s2 = stepY, s3 = stepK;

                    // Sort network for 4 elements (descending) - 5 comparisons
                    if (f0 < f1)
                    {
                        (f0, f1) = (f1, f0);
                        (s0, s1) = (s1, s0);
                    }

                    if (f2 < f3)
                    {
                        (f2, f3) = (f3, f2);
                        (s2, s3) = (s3, s2);
                    }

                    if (f0 < f2)
                    {
                        (f0, f2) = (f2, f0);
                        (s0, s2) = (s2, s0);
                    }

                    if (f1 < f3)
                    {
                        (f1, f3) = (f3, f1);
                        (s1, s3) = (s3, s1);
                    }

                    if (f1 < f2)
                    {
                        (f1, f2) = (f2, f1);
                        (s1, s2) = (s2, s1);
                    }

                    //  Tetrahedral interpolation through the sorted simplex.
                    //  Result = (255-f0)*V0 + (f0-f1)*V1 + (f1-f2)*V2 + (f2-f3)*V3 + f3*V4, all divided by 255.
                    int w0 = 255 - f0;
                    int w1 = f0 - f1;
                    int w2 = f1 - f2;
                    int w3 = f2 - f3;
                    int w4 = f3;

                    int off0 = baseOff;
                    int off1 = off0 + s0;
                    int off2 = off1 + s1;
                    int off3 = off2 + s2;
                    int off4 = off3 + s3;

                    byte* p0 = pSamples + off0;
                    byte* p1 = pSamples + off1;
                    byte* p2 = pSamples + off2;
                    byte* p3 = pSamples + off3;
                    byte* p4 = pSamples + off4;

                    // +127 for rounding (half of 255)
                    outR = (byte)((w0 * p0[0] + w1 * p1[0] + w2 * p2[0] + w3 * p3[0] + w4 * p4[0] + 127) / 255);
                    outG = (byte)((w0 * p0[1] + w1 * p1[1] + w2 * p2[1] + w3 * p3[1] + w4 * p4[1] + 127) / 255);
                    outB = (byte)((w0 * p0[2] + w1 * p1[2] + w2 * p2[2] + w3 * p3[2] + w4 * p4[2] + 127) / 255);

                    // Update cache
                    uint rgb = (uint)outR | ((uint)outG << 8) | ((uint)outB << 16);
                    pCache[hash] = cmyk | ((ulong)rgb << 32);
                }

                // Final write block
                byte aa = hasAlpha ? pA[i] : (byte)255;
                if (premul && aa < 255)
                {
                    outR = PremultiplyFast(outR, aa);
                    outG = PremultiplyFast(outG, aa);
                    outB = PremultiplyFast(outB, aa);
                }

                if (isBgra)
                {
                    pDst[di + 0] = outB;
                    pDst[di + 1] = outG;
                    pDst[di + 2] = outR;
                    pDst[di + 3] = aa;
                }
                else
                {
                    pDst[di + 0] = outR;
                    pDst[di + 1] = outG;
                    pDst[di + 2] = outB;
                    pDst[di + 3] = aa;
                }
            }
        }
    }

    /// <summary>
    /// Pre-computed offset and weight tables for a given grid size.
    /// Build once, share across all rows (and threads). Thread-safe for reads.
    /// </summary>
    public sealed class CmykGridLookupTables
    {
        public readonly int[] OffC0, OffC1, OffM0, OffM1, OffY0, OffY1, OffK0, OffK1;
        public readonly int[] Frac; // fractional position [0..255] for each input byte

        public CmykGridLookupTables(int gridSize)
        {
            const int strideK = 3;
            var strideY = gridSize * strideK;
            var strideM = gridSize * strideY;
            var strideC = gridSize * strideM;

            OffC0 = new int[256];
            OffC1 = new int[256];
            OffM0 = new int[256];
            OffM1 = new int[256];
            OffY0 = new int[256];
            OffY1 = new int[256];
            OffK0 = new int[256];
            OffK1 = new int[256];
            Frac = new int[256];

            for (int v = 0; v < 256; v++)
            {
                ComputeGridIndices(v, gridSize, strideC, out OffC0[v], out OffC1[v]);
                ComputeGridIndices(v, gridSize, strideM, out OffM0[v], out OffM1[v]);
                ComputeGridIndices(v, gridSize, strideY, out OffY0[v], out OffY1[v]);
                ComputeGridIndices(v, gridSize, strideK, out OffK0[v], out OffK1[v]);

                // Store fractional position for tetrahedral sort (scaled to [0..255])
                int scaled = v * (gridSize - 1);
                int idx0 = scaled / 255;
                if (idx0 >= gridSize - 1)
                    Frac[v] = 0;
                else
                    Frac[v] = scaled - idx0 * 255;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ComputeGridIndices(int v, int gridSize, int stride, out int off0, out int off1)
        {
            int scaled = v * (gridSize - 1);
            int idx0 = scaled / 255;
            if (idx0 >= gridSize - 1)
            {
                idx0 = gridSize - 1;
                off0 = idx0 * stride;
                off1 = off0;
            }
            else
            {
                off0 = idx0 * stride;
                off1 = (idx0 + 1) * stride;
            }
        }
    }

    #endregion

    #region Grayscale

    public static void WriteGrayRow(
        Span<byte> dstRow,
        PixelFormat dstPixelFormat,
        AlphaType alphaType,
        ReadOnlySpan<byte> grayRow,
        ReadOnlySpan<byte> aRow,
        bool hasAlpha,
        bool useCalibration,
        RgbLuts cal
        )
    {
        Debug.Assert(!hasAlpha || aRow.Length >= grayRow.Length, "Alpha row must be at least as long as grayscale row when hasAlpha=true.");
        Debug.Assert(dstRow.Length >= grayRow.Length * 4, "Destination row span too small.");

        var isBgra = dstPixelFormat == PixelFormat.Bgra8888;
        var isRgba = dstPixelFormat == PixelFormat.Rgba8888;
        if (!isBgra && !isRgba)
            throw new NotSupportedException($"Unsupported PixelFormat: {dstPixelFormat}");

        var isPremul = hasAlpha && alphaType == AlphaType.Premultiplied;
        var width = grayRow.Length;

        byte[]? lutR = null, lutG = null, lutB = null;
        var useLut = useCalibration;
        if (useLut)
        {
            lutR = cal.R;
            lutG = cal.G;
            lutB = cal.B;

            if (lutR is null || lutG is null || lutB is null)
                throw new ArgumentNullException(nameof(cal), "Calibration LUTs are required when useCalibration=true.");

            if (lutR.Length < 256 || lutG.Length < 256 || lutB.Length < 256)
                throw new ArgumentException("Calibration LUTs must have length >= 256.", nameof(cal));
        }

        for (int i = 0, dstIdx = 0; i < width; i++, dstIdx += 4)
        {
            byte c = grayRow[i];
            byte r = useLut ? lutR![c] : c;
            byte g = useLut ? lutG![c] : c;
            byte b = useLut ? lutB![c] : c;

            byte a = hasAlpha ? aRow[i] : (byte)255;

            if (isPremul && a < 255)
            {
                r = PremultiplyFast(r, a);
                g = PremultiplyFast(g, a);
                b = PremultiplyFast(b, a);
            }

            if (isBgra)
            {
                dstRow[dstIdx + 0] = b;
                dstRow[dstIdx + 1] = g;
                dstRow[dstIdx + 2] = r;
                dstRow[dstIdx + 3] = a;
            }
            else
            {
                dstRow[dstIdx + 0] = r;
                dstRow[dstIdx + 1] = g;
                dstRow[dstIdx + 2] = b;
                dstRow[dstIdx + 3] = a;
            }
        }
    }

    #endregion

    #region Indexed

    public static void WriteIndexedRow(
        Span<byte> dstRow,
        PixelFormat dstPixelFormat,
        AlphaType alphaType,
        ReadOnlySpan<byte> indexRow,
        ReadOnlySpan<byte> aRow,
        ReadOnlySpan<byte> palette,
        bool hasAlpha
        )
    {
        Debug.Assert(palette.Length >= 768, "Palette must have at least 768 bytes (256 * 3).");
        Debug.Assert(!hasAlpha || aRow.Length >= indexRow.Length, "Alpha row must be at least as long as index row when hasAlpha=true.");
        Debug.Assert(dstRow.Length >= indexRow.Length * 4, "Destination row span too small.");

        var isBgra = dstPixelFormat == PixelFormat.Bgra8888;
        var isRgba = dstPixelFormat == PixelFormat.Rgba8888;
        if (!isBgra && !isRgba)
            throw new NotSupportedException($"Unsupported PixelFormat: {dstPixelFormat}");

        var width = indexRow.Length;
        var isPremul = hasAlpha && alphaType == AlphaType.Premultiplied;

        const bool assumePlanar = true;

        for (int i = 0, dstIdx = 0; i < width; i++, dstIdx += 4)
        {
            var idx = indexRow[i];

            byte r, g, b;
            if (assumePlanar)
            {
                ReadPaletteRgbPlanar(palette, idx, out r, out g, out b);
            }
            else
            {
                ReadPaletteRgbInterleaved(palette, idx, out r, out g, out b);
            }

            byte a = hasAlpha ? aRow[i] : (byte)255;

            if (isPremul && a < 255)
            {
                r = PremultiplyFast(r, a);
                g = PremultiplyFast(g, a);
                b = PremultiplyFast(b, a);
            }

            if (isBgra)
            {
                dstRow[dstIdx + 0] = b;
                dstRow[dstIdx + 1] = g;
                dstRow[dstIdx + 2] = r;
                dstRow[dstIdx + 3] = a;
            }
            else
            {
                dstRow[dstIdx + 0] = r;
                dstRow[dstIdx + 1] = g;
                dstRow[dstIdx + 2] = b;
                dstRow[dstIdx + 3] = a;
            }
        }
    }

    public static void WriteIndexedRowInterleaved(
        Span<byte> dstRow,
        PixelFormat dstPixelFormat,
        AlphaType alphaType,
        ReadOnlySpan<byte> indexRow,
        ReadOnlySpan<byte> aRow,
        ReadOnlySpan<byte> paletteRgbInterleaved,
        bool hasAlpha
    )
    {
        Debug.Assert(paletteRgbInterleaved.Length >= 768, "Palette must have at least 768 bytes (256 * 3).");
        Debug.Assert(!hasAlpha || aRow.Length >= indexRow.Length);
        Debug.Assert(dstRow.Length >= indexRow.Length * 4);

        var isBgra = dstPixelFormat == PixelFormat.Bgra8888;
        var isRgba = dstPixelFormat == PixelFormat.Rgba8888;
        if (!isBgra && !isRgba)
            throw new NotSupportedException($"Unsupported PixelFormat: {dstPixelFormat}");

        var width = indexRow.Length;
        var isPremul = hasAlpha && alphaType == AlphaType.Premultiplied;

        for (int i = 0, dstIdx = 0; i < width; i++, dstIdx += 4)
        {
            var idx = indexRow[i];

            ReadPaletteRgbInterleaved(paletteRgbInterleaved, idx, out var r, out var g, out var b);

            byte a = hasAlpha ? aRow[i] : (byte)255;

            if (isPremul && a < 255)
            {
                r = PremultiplyFast(r, a);
                g = PremultiplyFast(g, a);
                b = PremultiplyFast(b, a);
            }

            if (isBgra)
            {
                dstRow[dstIdx + 0] = b;
                dstRow[dstIdx + 1] = g;
                dstRow[dstIdx + 2] = r;
                dstRow[dstIdx + 3] = a;
            }
            else
            {
                dstRow[dstIdx + 0] = r;
                dstRow[dstIdx + 1] = g;
                dstRow[dstIdx + 2] = b;
                dstRow[dstIdx + 3] = a;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadPaletteRgbPlanar(ReadOnlySpan<byte> palette, byte idx, out byte r, out byte g, out byte b)
    {
        // Planar: [R(256)][G(256)][B(256)]
        var i = idx;
        r = palette[i];
        g = palette[i + 256];
        b = palette[i + 512];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReadPaletteRgbInterleaved(ReadOnlySpan<byte> palette, byte idx, out byte r, out byte g, out byte b)
    {
        // Interleaved: [R0 G0 B0][R1 G1 B1]...
        var baseIdx = idx * 3;
        r = palette[baseIdx + 0];
        g = palette[baseIdx + 1];
        b = palette[baseIdx + 2];
    }

    #endregion

    #region Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte PremultiplyFast(byte c, byte a) => (byte)(((c * a + 128) * 257) >> 16);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampToByte(int value)
    {
        if ((uint)value <= 255)
            return (byte)value;

        return (byte)(value < 0 ? 0 : 255);
    }

    #endregion
}