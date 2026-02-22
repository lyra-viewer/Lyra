using System.Buffers;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.ColorCalibration;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.SectionData;
using Wacton.Unicolour;

namespace Lyra.Imaging.Psd.Core.Decode.ColorProcessors;

public sealed class CmykProcessor : IColorModeProcessor
{
    public string? IccProfileUsed { get; private set; }

    // Tuning knobs
    private const float OutputGamma = 0.95f;
    private const bool EnableNeutralBalance = true;

    // How "neutral" a pixel must be to qualify for neutral-balance correction (0..255).
    // Lower => affects fewer pixels (safer).
    private const int NeutralThreshold = 20;

    // Neutral-balance correction, applied only to near-neutrals.
    private const int NeutralRAdd = 8;
    private const int NeutralGAdd = -6;
    private const int NeutralBAdd = 0;

    private static readonly byte[] GammaLut = BuildGammaLut(OutputGamma);

    private static readonly IccCalibrationProvider CalibrationProvider = new();

    public RgbaSurface Process(PlaneImage src, ColorModeContext ctx, ColorModeData? colorModeData, CancellationToken ct)
    {
        var depth = PsdDepthUtil.FromBitsPerChannel(src.BitsPerChannel);
        var bpcBytes = depth.BytesPerChannel();
        var rowBytes = depth.RowBytes(src.Width);

        var c = src.GetPlaneOrThrow(PlaneRole.C);
        var m = src.GetPlaneOrThrow(PlaneRole.M);
        var y = src.GetPlaneOrThrow(PlaneRole.Y);
        var k = src.GetPlaneOrThrow(PlaneRole.K);
        
        src.TryGetPlane(PlaneRole.A, out var a);
        
        if (c.BytesPerRow < rowBytes || m.BytesPerRow < rowBytes || y.BytesPerRow < rowBytes || k.BytesPerRow < rowBytes)
            throw new InvalidOperationException("Plane BytesPerRow is smaller than Width*bpc for CMYK.");

        if (a.Data != null && a.BytesPerRow < rowBytes)
            throw new InvalidOperationException("Alpha plane BytesPerRow is smaller than Width*bpc.");

        var dstPixelFormat = ctx.OutputFormat.PixelFormat;
        var alphaType = ctx.OutputFormat.AlphaType;

        var stride = checked(src.Width * 4);
        var size = checked(stride * src.Height);

        const int gridSize = ColorCalibrationDefaults.GridSize;
        var calibration = CalibrationProvider.GetCalibration(
            new ColorCalibrationRequest(
                SourceColorMode: ColorMode.Cmyk,
                EmbeddedIccProfile: ctx.IccProfile,
                PreferColorManagement: ctx.PreferColorManagement,
                GridSize: gridSize),
            config => BuildCalibrationLuts(src, c, m, y, k, config, gridSize, ct),
            out var iccProfileUsed
        );

        IccProfileUsed = iccProfileUsed;

        var useHeuristicTuning = !ctx.PreferColorManagement || calibration.IsIdentity;
        var tuning = new PixelRowWriter.CmykHeuristicTuning(
            GammaLut,
            EnableNeutralBalance,
            NeutralThreshold,
            NeutralRAdd,
            NeutralGAdd,
            NeutralBAdd);

        var owner = MemoryPool<byte>.Shared.Rent(size);
        var surface = new RgbaSurface(src.Width, src.Height, owner, stride, ctx.OutputFormat);

        byte[]? c8Rent = null;
        byte[]? m8Rent = null;
        byte[]? y8Rent = null;
        byte[]? k8Rent = null;
        byte[]? a8Rent = null;

        if (bpcBytes != 1)
        {
            c8Rent = ArrayPool<byte>.Shared.Rent(src.Width);
            m8Rent = ArrayPool<byte>.Shared.Rent(src.Width);
            y8Rent = ArrayPool<byte>.Shared.Rent(src.Width);
            k8Rent = ArrayPool<byte>.Shared.Rent(src.Width);
            if (a.Data != null)
                a8Rent = ArrayPool<byte>.Shared.Rent(src.Width);
        }

        try
        {
            for (var row = 0; row < src.Height; row++)
            {
                ct.ThrowIfCancellationRequested();

                var cRowRaw = c.Data.AsSpan(row * c.BytesPerRow, rowBytes);
                var mRowRaw = m.Data.AsSpan(row * m.BytesPerRow, rowBytes);
                var yRowRaw = y.Data.AsSpan(row * y.BytesPerRow, rowBytes);
                var kRowRaw = k.Data.AsSpan(row * k.BytesPerRow, rowBytes);

                ReadOnlySpan<byte> cRow8;
                ReadOnlySpan<byte> mRow8;
                ReadOnlySpan<byte> yRow8;
                ReadOnlySpan<byte> kRow8;
                ReadOnlySpan<byte> aRow8 = default;

                var hasAlpha = a.Data != null && a.Data.Length != 0;

                if (bpcBytes == 1)
                {
                    cRow8 = cRowRaw[..src.Width];
                    mRow8 = mRowRaw[..src.Width];
                    yRow8 = yRowRaw[..src.Width];
                    kRow8 = kRowRaw[..src.Width];

                    if (hasAlpha)
                        aRow8 = a.Data.AsSpan(row * a.BytesPerRow, src.Width);
                }
                else
                {
                    var cc = c8Rent!.AsSpan(0, src.Width);
                    var mm = m8Rent!.AsSpan(0, src.Width);
                    var yy = y8Rent!.AsSpan(0, src.Width);
                    var kk = k8Rent!.AsSpan(0, src.Width);

                    if (bpcBytes == 2)
                    {
                        PsdSampleConvert.Row16BeTo8(cc, cRowRaw);
                        PsdSampleConvert.Row16BeTo8(mm, mRowRaw);
                        PsdSampleConvert.Row16BeTo8(yy, yRowRaw);
                        PsdSampleConvert.Row16BeTo8(kk, kRowRaw);

                        if (hasAlpha)
                        {
                            var aRaw = a.Data.AsSpan(row * a.BytesPerRow, rowBytes);
                            var aa = a8Rent!.AsSpan(0, src.Width);
                            PsdSampleConvert.Row16BeTo8(aa, aRaw);
                            aRow8 = aa;
                        }
                    }
                    else
                    {
                        PsdSampleConvert.Row32FloatBeTo8(cc, cRowRaw);
                        PsdSampleConvert.Row32FloatBeTo8(mm, mRowRaw);
                        PsdSampleConvert.Row32FloatBeTo8(yy, yRowRaw);
                        PsdSampleConvert.Row32FloatBeTo8(kk, kRowRaw);

                        if (hasAlpha)
                        {
                            var aRaw = a.Data.AsSpan(row * a.BytesPerRow, rowBytes);
                            var aa = a8Rent!.AsSpan(0, src.Width);
                            PsdSampleConvert.Row32FloatBeTo8(aa, aRaw);
                            aRow8 = aa;
                        }
                    }

                    cRow8 = cc;
                    mRow8 = mm;
                    yRow8 = yy;
                    kRow8 = kk;
                }

                var dstRow = surface.GetRowSpan(row);

                PixelRowWriter.WriteCmykRow(
                    dstRow,
                    dstPixelFormat,
                    alphaType,
                    cRow8,
                    mRow8,
                    yRow8,
                    kRow8,
                    hasAlpha ? aRow8 : default,
                    hasAlpha,
                    useHeuristicTuning,
                    tuning,
                    calibration);
            }

            return surface;
        }
        catch
        {
            surface.Dispose();
            throw;
        }
        finally
        {
            if (c8Rent != null) ArrayPool<byte>.Shared.Return(c8Rent);
            if (m8Rent != null) ArrayPool<byte>.Shared.Return(m8Rent);
            if (y8Rent != null) ArrayPool<byte>.Shared.Return(y8Rent);
            if (k8Rent != null) ArrayPool<byte>.Shared.Return(k8Rent);
            if (a8Rent != null) ArrayPool<byte>.Shared.Return(a8Rent);
        }
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
        if (iv < 0)
            iv = 0;

        if (iv > 255)
            iv = 255;

        return (byte)iv;
    }

    private static byte[] BuildGammaLut(float gamma)
    {
        var lut = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var x = i / 255f;
            var y = MathF.Pow(x, gamma);
            var v = (int)(y * 255f + 0.5f);
            if (v < 0)
                v = 0;

            if (v > 255)
                v = 255;

            lut[i] = (byte)v;
        }

        return lut;
    }

    private static RgbLuts BuildCalibrationLuts(
        PlaneImage src,
        Plane c, Plane m, Plane y, Plane k,
        Configuration config,
        int gridSize,
        CancellationToken ct)
    {
        var bpcBytes = src.BitsPerChannel switch
        {
            8 => 1,
            16 => 2,
            32 => 4,
            _ => throw new NotSupportedException($"Calibration: BitsPerChannel {src.BitsPerChannel} not supported (expected 8/16/32).")
        };

        var rowBytes = checked(src.Width * bpcBytes);

        var sumR = new int[256];
        var sumG = new int[256];
        var sumB = new int[256];
        var cntR = new int[256];
        var cntG = new int[256];
        var cntB = new int[256];

        const bool decidePolarityOnce = true;
        bool? useInverted = null;

        for (var gy = 0; gy < gridSize; gy++)
        {
            ct.ThrowIfCancellationRequested();
            var row = (int)((gy + 0.5) * src.Height / gridSize);
            if (row >= src.Height) row = src.Height - 1;

            var cRow = c.Data.AsSpan(row * c.BytesPerRow, rowBytes);
            var mRow = m.Data.AsSpan(row * m.BytesPerRow, rowBytes);
            var yRow = y.Data.AsSpan(row * y.BytesPerRow, rowBytes);
            var kRow = k.Data.AsSpan(row * k.BytesPerRow, rowBytes);

            for (var gx = 0; gx < gridSize; gx++)
            {
                var col = (int)((gx + 0.5) * src.Width / gridSize);
                if (col >= src.Width)
                    col = src.Width - 1;

                var off = col * bpcBytes;

                var c0 = PsdSampleConvert.SampleTo8(cRow, off, bpcBytes);
                var m0 = PsdSampleConvert.SampleTo8(mRow, off, bpcBytes);
                var y0 = PsdSampleConvert.SampleTo8(yRow, off, bpcBytes);
                var k0 = PsdSampleConvert.SampleTo8(kRow, off, bpcBytes);

                ConvertCmykApprox(c0, m0, y0, k0, out var r0, out var g0, out var b0);

                if (useInverted is null || !decidePolarityOnce)
                {
                    var rgbA = IccOracle.OracleIccRgb(config, c0, m0, y0, k0, invert: false);
                    var rgbB = IccOracle.OracleIccRgb(config, c0, m0, y0, k0, invert: true);

                    // Choose the variant closest to the baseline output
                    var dA = DistSq(rgbA.r, rgbA.g, rgbA.b, r0, g0, b0);
                    var dB = DistSq(rgbB.r, rgbB.g, rgbB.b, r0, g0, b0);

                    useInverted = dB < dA;
                }

                var rgb = IccOracle.OracleIccRgb(config, c0, m0, y0, k0, invert: useInverted.Value);

                sumR[r0] += rgb.r;
                cntR[r0]++;

                sumG[g0] += rgb.g;
                cntG[g0]++;

                sumB[b0] += rgb.b;
                cntB[b0]++;
            }
        }

        return LutBuilder.BuildRgbCurves(sumR, cntR, sumG, cntG, sumB, cntB);
    }

    private static int DistSq(int r1, int g1, int b1, byte r0, byte g0, byte b0)
    {
        var dr = r1 - r0;
        var dg = g1 - g0;
        var db = b1 - b0;
        return dr * dr + dg * dg + db * db;
    }
}