using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lyra.Imaging.Psd.Core.Decode.ColorCalibration;

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
        RgbLuts cal)
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
        bool hasAlpha)
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
        bool hasAlpha)
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
    private static byte PremultiplyFast(byte c, byte a)
        => (byte)(((c * a + 128) * 257) >> 16);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampToByte(int value)
    {
        if ((uint)value <= 255)
            return (byte)value;

        return (byte)(value < 0 ? 0 : 255);
    }

    #endregion
}