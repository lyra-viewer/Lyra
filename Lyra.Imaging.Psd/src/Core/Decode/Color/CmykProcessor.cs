using System.Buffers;
using Lyra.Imaging.Psd.Core.Common;
using Lyra.Imaging.Psd.Core.Decode.Color.ColorCalibration;
using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Wacton.Unicolour;
using Wacton.Unicolour.Icc;

namespace Lyra.Imaging.Psd.Core.Decode.Color;

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

    public RgbaSurface Process(PlaneImage src, ColorModeContext ctx, CancellationToken ct)
    {
        if (src.BitsPerChannel != 8)
            throw new NotSupportedException($"CMYK processor currently supports only 8 bits/channel, got {src.BitsPerChannel}.");

        var c = src.GetPlaneOrThrow(PlaneRole.C);
        var m = src.GetPlaneOrThrow(PlaneRole.M);
        var y = src.GetPlaneOrThrow(PlaneRole.Y);
        var k = src.GetPlaneOrThrow(PlaneRole.K);

        src.TryGetPlane(PlaneRole.A, out var a);

        if (c.BytesPerRow < src.Width || m.BytesPerRow < src.Width || y.BytesPerRow < src.Width || k.BytesPerRow < src.Width)
            throw new InvalidOperationException("Plane BytesPerRow is smaller than Width for 8bpc CMYK.");

        if (a.Data != null && a.BytesPerRow < src.Width)
            throw new InvalidOperationException("Alpha plane BytesPerRow is smaller than Width for 8bpc.");

        var dstPixelFormat = ctx.OutputFormat.PixelFormat;
        var alphaType = ctx.OutputFormat.AlphaType;

        var stride = checked(src.Width * 4);
        var size = checked(stride * src.Height);

        var gridSize = ColorCalibrationDefaults.GridSize;
        var calibration = CalibrationProvider.GetCalibration(
            new ColorCalibrationRequest(
                SourceColorMode: ColorMode.Cmyk,
                EmbeddedIccProfile: ctx.IccProfile,
                PreferColorManagement: ctx.PreferColorManagement,
                GridSize: gridSize),
            config => BuildCalibrationLuts(
                src, c, m, y, k,
                config,
                gridSize: gridSize,
                ct), out var iccProfileUsed
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
        var surface = new RgbaSurface(src.Width, src.Height, owner, stride);

        try
        {
            for (var row = 0; row < src.Height; row++)
            {
                ct.ThrowIfCancellationRequested();

                var cRow = c.Data.AsSpan(row * c.BytesPerRow, src.Width);
                var mRow = m.Data.AsSpan(row * m.BytesPerRow, src.Width);
                var yRow = y.Data.AsSpan(row * y.BytesPerRow, src.Width);
                var kRow = k.Data.AsSpan(row * k.BytesPerRow, src.Width);

                Span<byte> aRow = default;
                var hasAlpha = a.Data != null && a.Data.Length != 0;
                if (hasAlpha)
                    aRow = a.Data.AsSpan(row * a.BytesPerRow, src.Width);

                var dstRow = surface.GetRowSpan(row);

                PixelRowWriter.WriteCmykRow(
                    dstRow,
                    dstPixelFormat,
                    alphaType,
                    cRow,
                    mRow,
                    yRow,
                    kRow,
                    hasAlpha ? aRow : default,
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
        var sumR = new int[256];
        var sumG = new int[256];
        var sumB = new int[256];
        var cntR = new int[256];
        var cntG = new int[256];
        var cntB = new int[256];

        // Optional: decide polarity once per profile (slightly faster + more stable).
        // If per-sample is preferred (more robust for weird files), set this to false.
        const bool decidePolarityOnce = true;
        bool? useInverted = null;

        for (var gy = 0; gy < gridSize; gy++)
        {
            ct.ThrowIfCancellationRequested();
            var row = (int)((gy + 0.5) * src.Height / gridSize);
            if (row >= src.Height) row = src.Height - 1;

            var cRow = c.Data.AsSpan(row * c.BytesPerRow, src.Width);
            var mRow = m.Data.AsSpan(row * m.BytesPerRow, src.Width);
            var yRow = y.Data.AsSpan(row * y.BytesPerRow, src.Width);
            var kRow = k.Data.AsSpan(row * k.BytesPerRow, src.Width);

            for (var gx = 0; gx < gridSize; gx++)
            {
                var col = (int)((gx + 0.5) * src.Width / gridSize);
                if (col >= src.Width) 
                    col = src.Width - 1;

                var c0 = cRow[col];
                var m0 = mRow[col];
                var y0 = yRow[col];
                var k0 = kRow[col];

                ConvertCmykApprox(c0, m0, y0, k0, out var r0, out var g0, out var b0);

                if (useInverted is null || !decidePolarityOnce)
                {
                    var rgbA = OracleIccRgb(config, c0, m0, y0, k0, invert: false);
                    var rgbB = OracleIccRgb(config, c0, m0, y0, k0, invert: true);

                    // Choose the variant closest to the baseline output
                    var dA = DistSq(rgbA.r, rgbA.g, rgbA.b, r0, g0, b0);
                    var dB = DistSq(rgbB.r, rgbB.g, rgbB.b, r0, g0, b0);

                    useInverted = dB < dA;
                }

                var rgb = OracleIccRgb(config, c0, m0, y0, k0, invert: useInverted.Value);

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

    private static (int r, int g, int b) OracleIccRgb(Configuration config, byte c, byte m, byte y, byte k, bool invert)
    {
        var c0 = invert ? (255 - c) / 255.0 : c / 255.0;
        var m0 = invert ? (255 - m) / 255.0 : m / 255.0;
        var y0 = invert ? (255 - y) / 255.0 : y / 255.0;
        var k0 = invert ? (255 - k) / 255.0 : k / 255.0;

        var u = new Unicolour(config, new Channels(c0, m0, y0, k0));
        var rgb = u.Rgb.Byte255;

        return (Clamp0To255(rgb.R), Clamp0To255(rgb.G), Clamp0To255(rgb.B));

        static int Clamp0To255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
    }

    private static int DistSq(int r1, int g1, int b1, byte r0, byte g0, byte b0)
    {
        var dr = r1 - r0;
        var dg = g1 - g0;
        var db = b1 - b0;
        return dr * dr + dg * dg + db * db;
    }
}